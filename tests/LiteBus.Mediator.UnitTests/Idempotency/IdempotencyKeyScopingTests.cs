using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Runtime.Abstractions.Exceptions;
using LiteBus.Testing;
using LiteBus.Testing.Mediation;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Mediator.UnitTests.Idempotency;

/// <summary>
///     Verifies resolved-key scoping: full type names, tenant prefixes, separator escaping, blank-scope rejection, and
///     settle without re-resolution.
/// </summary>
[Collection("Sequential")]
public sealed class IdempotencyKeyScopingTests : LiteBusTestBase
{
    [Fact]
    public async Task Commands_with_the_same_simple_name_do_not_share_a_key_space()
    {
        var applications = new ApplicationCounter();
        var store = new InMemoryIdempotencyStore();
        var provider = BuildProvider(
            store,
            applications,
            static builder =>
            {
                builder.Register<Billing.SubmitOrderCommand>();
                builder.Register<Billing.SubmitOrderCommandHandler>();
                builder.Register<Billing.SubmitOrderCommandDefinition>();
                builder.Register<Shipping.SubmitOrderCommand>();
                builder.Register<Shipping.SubmitOrderCommandHandler>();
                builder.Register<Shipping.SubmitOrderCommandDefinition>();
            });
        var mediator = provider.GetRequiredService<ICommandMediator>();

        await mediator.SendAsync(new Billing.SubmitOrderCommand { OrderId = "5" }).ConfigureAwait(false);
        await mediator.SendAsync(new Shipping.SubmitOrderCommand { OrderId = "5" }).ConfigureAwait(false);

        applications.Count.Should().Be(2);
        store.AppliedKeys.Should().Contain($"{typeof(Billing.SubmitOrderCommand).FullName}:5");
        store.AppliedKeys.Should().Contain($"{typeof(Shipping.SubmitOrderCommand).FullName}:5");
    }

    [Fact]
    public async Task The_same_key_under_different_tenants_runs_for_each_tenant()
    {
        var applications = new ApplicationCounter();
        var provider = BuildProvider(
            new InMemoryIdempotencyStore(),
            applications,
            static builder =>
            {
                builder.Register<ApplyPaymentCommand>();
                builder.Register<ApplyPaymentCommandHandler>();
                builder.Register<ApplyPaymentCommandDefinition>();
            });
        var mediator = provider.GetRequiredService<ICommandMediator>();

        var tenantA = new CommandMediationSettings();
        tenantA.Items[MessageTraceContextKeys.TenantId] = "tenant-a";
        var tenantB = new CommandMediationSettings();
        tenantB.Items[MessageTraceContextKeys.TenantId] = "tenant-b";

        await mediator.SendAsync(new ApplyPaymentCommand { PaymentId = "order-1" }, tenantA).ConfigureAwait(false);
        await mediator.SendAsync(new ApplyPaymentCommand { PaymentId = "order-1" }, tenantB).ConfigureAwait(false);

        applications.Count.Should().Be(2);

        var repeat = await mediator
            .TrySendAsync(new ApplyPaymentCommand { PaymentId = "order-1" }, tenantA).ConfigureAwait(false);

        repeat.Outcome.Should().Be(MediationOutcome.Answered);
        applications.Count.Should().Be(2);
    }

    [Fact]
    public async Task Scope_and_key_segments_containing_the_separator_do_not_collide()
    {
        var applications = new ApplicationCounter();
        var provider = BuildProvider(
            new InMemoryIdempotencyStore(),
            applications,
            static builder =>
            {
                builder.Register<RegionalBillingCommand>();
                builder.Register<RegionalBillingCommandHandler>();
                builder.Register<RegionalBillingCommandDefinition>();
                builder.Register<QualifiedBillingCommand>();
                builder.Register<QualifiedBillingCommandHandler>();
                builder.Register<QualifiedBillingCommandDefinition>();
            });
        var mediator = provider.GetRequiredService<ICommandMediator>();

        // Scope "billing" with key "eu:5" would join to the same string as scope "billing:eu" with key "5" if the
        // segments were concatenated raw.
        await mediator.SendAsync(new RegionalBillingCommand { Region = "eu", Number = "5" }).ConfigureAwait(false);
        await mediator.SendAsync(new QualifiedBillingCommand { Number = "5" }).ConfigureAwait(false);

        applications.Count.Should().Be(2);
    }

    [Fact]
    public async Task A_blank_scope_is_reported_rather_than_shared()
    {
        var applications = new ApplicationCounter();
        var provider = BuildProvider(
            new InMemoryIdempotencyStore(),
            applications,
            static builder =>
            {
                builder.Register<BlankScopeCommand>();
                builder.Register<BlankScopeCommandHandler>();
                builder.Register<BlankScopeCommandDefinition>();
            });

        var act = async () => await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new BlankScopeCommand { PaymentId = "p-1" }).ConfigureAwait(false);

        await act.Should().ThrowAsync<IdempotencyConfigurationException>()
            .WithMessage("*scope*").ConfigureAwait(false);
        applications.Count.Should().Be(0);
    }

    [Fact]
    public async Task An_unknown_claim_outcome_fails_loudly_for_a_void_command()
    {
        var applications = new ApplicationCounter();
        var provider = BuildProvider(
            new UnknownOutcomeStore(),
            applications,
            static builder =>
            {
                builder.Register<ApplyPaymentCommand>();
                builder.Register<ApplyPaymentCommandHandler>();
                builder.Register<ApplyPaymentCommandDefinition>();
            });

        var act = async () => await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new ApplyPaymentCommand { PaymentId = "p-1" }).ConfigureAwait(false);

        await act.Should().ThrowAsync<LiteBusConfigurationException>()
            .WithMessage("*unrecognized outcome*").ConfigureAwait(false);
        applications.Count.Should().Be(0);
    }

    [Fact]
    public async Task An_unknown_claim_outcome_fails_loudly_for_a_result_command()
    {
        var applications = new ApplicationCounter();
        var provider = BuildProvider(
            new UnknownOutcomeStore(),
            applications,
            static builder =>
            {
                builder.Register<RepeatablePaymentCommand>();
                builder.Register<RepeatablePaymentCommandHandler>();
                builder.Register<RepeatablePaymentCommandDefinition>();
            });

        var act = async () => await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new RepeatablePaymentCommand { PaymentId = "p-1" }).ConfigureAwait(false);

        await act.Should().ThrowAsync<LiteBusConfigurationException>()
            .WithMessage("*unrecognized outcome*").ConfigureAwait(false);
        applications.Count.Should().Be(0);
    }

    [Fact]
    public async Task A_handler_that_mutates_the_key_still_settles_its_claim()
    {
        var applications = new ApplicationCounter();
        var provider = BuildProvider(
            new InMemoryIdempotencyStore(),
            applications,
            static builder =>
            {
                builder.Register<MutatingPaymentCommand>();
                builder.Register<MutatingPaymentCommandHandler>();
                builder.Register<MutatingPaymentCommandDefinition>();
            });
        var mediator = provider.GetRequiredService<ICommandMediator>();

        // The handler blanks the field the key was projected from. Settling re-resolves nothing, so the success the
        // handler produced is not turned into a configuration failure at completion time.
        await mediator.SendAsync(new MutatingPaymentCommand { PaymentId = "p-1" }).ConfigureAwait(false);

        applications.Count.Should().Be(1);

        var repeat = await mediator
            .TrySendAsync(new MutatingPaymentCommand { PaymentId = "p-1" }).ConfigureAwait(false);

        repeat.Outcome.Should().Be(MediationOutcome.Answered);
        applications.Count.Should().Be(1);
    }

    /// <summary>
    ///     Builds a provider with idempotency enabled and the given store registered.
    /// </summary>
    /// <param name="store">The store shared with the test.</param>
    /// <param name="applications">The counter recording how many times each handler actually ran.</param>
    /// <param name="configure">Registers the commands the test needs.</param>
    /// <returns>The configured service provider.</returns>
    private static ServiceProvider BuildProvider(
        IIdempotencyStore store,
        ApplicationCounter applications,
        Action<CommandModuleBuilder> configure)
    {
        var services = new ServiceCollection();
        services.AddSingleton(store);
        services.AddSingleton(applications);

        return services
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(_ => { });

                registry.AddCommands(builder =>
                {
                    builder.EnableIdempotency();
                    configure(builder);
                });
            })
            .BuildServiceProvider();
    }

    private sealed class UnknownOutcomeStore : IIdempotencyStore
    {
        public Task<IdempotencyClaim> TryClaimAsync(string key, string? fingerprint, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new IdempotencyClaim((IdempotencyClaimOutcome) 99));
        }

        public Task CompleteAsync(string key, string claimToken, string? payload, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task ReleaseAsync(string key, string claimToken, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }
}

/// <summary>
///     Holds a billing order command sharing its simple name with the shipping one.
/// </summary>
internal static class Billing
{
    /// <summary>
    ///     A billing order command with the same simple name as its shipping counterpart.
    /// </summary>
    internal sealed class SubmitOrderCommand : ICommand
    {
        /// <summary>
        ///     Gets or sets the order identifier the key is projected from.
        /// </summary>
        public string OrderId { get; set; } = string.Empty;
    }

    /// <summary>
    ///     Declares how a repeat of the billing command is recognised.
    /// </summary>
    internal sealed class SubmitOrderCommandDefinition : IIdempotencyDefinition<SubmitOrderCommand>
    {
        /// <inheritdoc />
        public IdempotencyDeclaration Idempotency =>
            IdempotencyDeclaration.KeyedBy<SubmitOrderCommand>(command => command.OrderId);
    }

    /// <summary>
    ///     Submits the billing order.
    /// </summary>
    internal sealed class SubmitOrderCommandHandler : ICommandHandler<SubmitOrderCommand>
    {
        /// <summary>
        ///     The counter shared with the test.
        /// </summary>
        private readonly ApplicationCounter _applications;

        /// <summary>
        ///     Initializes a new instance of the <see cref="SubmitOrderCommandHandler" /> class.
        /// </summary>
        /// <param name="applications">The counter shared with the test.</param>
        public SubmitOrderCommandHandler(ApplicationCounter applications)
        {
            _applications = applications;
        }

        /// <inheritdoc />
        public Task HandleAsync(SubmitOrderCommand message, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(message);
            _applications.Count++;
            return Task.CompletedTask;
        }
    }
}

/// <summary>
///     Holds a shipping order command sharing its simple name with the billing one.
/// </summary>
internal static class Shipping
{
    /// <summary>
    ///     A shipping order command with the same simple name as its billing counterpart.
    /// </summary>
    internal sealed class SubmitOrderCommand : ICommand
    {
        /// <summary>
        ///     Gets or sets the order identifier the key is projected from.
        /// </summary>
        public string OrderId { get; set; } = string.Empty;
    }

    /// <summary>
    ///     Declares how a repeat of the shipping command is recognised.
    /// </summary>
    internal sealed class SubmitOrderCommandDefinition : IIdempotencyDefinition<SubmitOrderCommand>
    {
        /// <inheritdoc />
        public IdempotencyDeclaration Idempotency =>
            IdempotencyDeclaration.KeyedBy<SubmitOrderCommand>(command => command.OrderId);
    }

    /// <summary>
    ///     Submits the shipping order.
    /// </summary>
    internal sealed class SubmitOrderCommandHandler : ICommandHandler<SubmitOrderCommand>
    {
        /// <summary>
        ///     The counter shared with the test.
        /// </summary>
        private readonly ApplicationCounter _applications;

        /// <summary>
        ///     Initializes a new instance of the <see cref="SubmitOrderCommandHandler" /> class.
        /// </summary>
        /// <param name="applications">The counter shared with the test.</param>
        public SubmitOrderCommandHandler(ApplicationCounter applications)
        {
            _applications = applications;
        }

        /// <inheritdoc />
        public Task HandleAsync(SubmitOrderCommand message, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(message);
            _applications.Count++;
            return Task.CompletedTask;
        }
    }
}

/// <summary>
///     A command scoped to <c>billing</c> whose key carries the separator.
/// </summary>
internal sealed class RegionalBillingCommand : ICommand
{
    /// <summary>
    ///     Gets or sets the region prefixing the key.
    /// </summary>
    public string Region { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets the number completing the key.
    /// </summary>
    public string Number { get; set; } = string.Empty;
}

/// <summary>
///     Declares the regional billing key with an explicit scope.
/// </summary>
internal sealed class RegionalBillingCommandDefinition : IIdempotencyDefinition<RegionalBillingCommand>
{
    /// <inheritdoc />
    public IdempotencyDeclaration Idempotency =>
        IdempotencyDeclaration.KeyedBy<RegionalBillingCommand>(command => $"{command.Region}:{command.Number}")
            with
        { Scope = "billing" };
}

/// <summary>
///     Bills the regional account.
/// </summary>
internal sealed class RegionalBillingCommandHandler : ICommandHandler<RegionalBillingCommand>
{
    /// <summary>
    ///     The counter shared with the test.
    /// </summary>
    private readonly ApplicationCounter _applications;

    /// <summary>
    ///     Initializes a new instance of the <see cref="RegionalBillingCommandHandler" /> class.
    /// </summary>
    /// <param name="applications">The counter shared with the test.</param>
    public RegionalBillingCommandHandler(ApplicationCounter applications)
    {
        _applications = applications;
    }

    /// <inheritdoc />
    public Task HandleAsync(RegionalBillingCommand message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        _applications.Count++;
        return Task.CompletedTask;
    }
}

/// <summary>
///     A command whose explicit scope already names the region.
/// </summary>
internal sealed class QualifiedBillingCommand : ICommand
{
    /// <summary>
    ///     Gets or sets the number the key is projected from.
    /// </summary>
    public string Number { get; set; } = string.Empty;
}

/// <summary>
///     Declares the qualified billing key with a scope containing the separator.
/// </summary>
internal sealed class QualifiedBillingCommandDefinition : IIdempotencyDefinition<QualifiedBillingCommand>
{
    /// <inheritdoc />
    public IdempotencyDeclaration Idempotency =>
        IdempotencyDeclaration.KeyedBy<QualifiedBillingCommand>(command => command.Number)
            with
        { Scope = "billing:eu" };
}

/// <summary>
///     Bills the qualified account.
/// </summary>
internal sealed class QualifiedBillingCommandHandler : ICommandHandler<QualifiedBillingCommand>
{
    /// <summary>
    ///     The counter shared with the test.
    /// </summary>
    private readonly ApplicationCounter _applications;

    /// <summary>
    ///     Initializes a new instance of the <see cref="QualifiedBillingCommandHandler" /> class.
    /// </summary>
    /// <param name="applications">The counter shared with the test.</param>
    public QualifiedBillingCommandHandler(ApplicationCounter applications)
    {
        _applications = applications;
    }

    /// <inheritdoc />
    public Task HandleAsync(QualifiedBillingCommand message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        _applications.Count++;
        return Task.CompletedTask;
    }
}

/// <summary>
///     A command declaring a blank explicit scope.
/// </summary>
internal sealed class BlankScopeCommand : ICommand
{
    /// <summary>
    ///     Gets or sets the payment identifier the key is projected from.
    /// </summary>
    public string PaymentId { get; set; } = string.Empty;
}

/// <summary>
///     Declares a blank scope for <see cref="BlankScopeCommand" />.
/// </summary>
internal sealed class BlankScopeCommandDefinition : IIdempotencyDefinition<BlankScopeCommand>
{
    /// <inheritdoc />
    public IdempotencyDeclaration Idempotency =>
        IdempotencyDeclaration.KeyedBy<BlankScopeCommand>(command => command.PaymentId)
            with
        { Scope = "" };
}

/// <summary>
///     Applies the payment, and must never run while the scope is rejected.
/// </summary>
internal sealed class BlankScopeCommandHandler : ICommandHandler<BlankScopeCommand>
{
    /// <summary>
    ///     The counter shared with the test.
    /// </summary>
    private readonly ApplicationCounter _applications;

    /// <summary>
    ///     Initializes a new instance of the <see cref="BlankScopeCommandHandler" /> class.
    /// </summary>
    /// <param name="applications">The counter shared with the test.</param>
    public BlankScopeCommandHandler(ApplicationCounter applications)
    {
        _applications = applications;
    }

    /// <inheritdoc />
    public Task HandleAsync(BlankScopeCommand message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        _applications.Count++;
        return Task.CompletedTask;
    }
}

/// <summary>
///     A command whose handler blanks the field the key was projected from.
/// </summary>
internal sealed class MutatingPaymentCommand : ICommand
{
    /// <summary>
    ///     Gets or sets the payment identifier the key is projected from.
    /// </summary>
    public string PaymentId { get; set; } = string.Empty;
}

/// <summary>
///     Declares how a repeat of <see cref="MutatingPaymentCommand" /> is recognised.
/// </summary>
internal sealed class MutatingPaymentCommandDefinition : IIdempotencyDefinition<MutatingPaymentCommand>
{
    /// <inheritdoc />
    public IdempotencyDeclaration Idempotency =>
        IdempotencyDeclaration.KeyedBy<MutatingPaymentCommand>(command => command.PaymentId);
}

/// <summary>
///     Applies the payment and blanks the key field, as a buggy normalization would.
/// </summary>
internal sealed class MutatingPaymentCommandHandler : ICommandHandler<MutatingPaymentCommand>
{
    /// <summary>
    ///     The counter shared with the test.
    /// </summary>
    private readonly ApplicationCounter _applications;

    /// <summary>
    ///     Initializes a new instance of the <see cref="MutatingPaymentCommandHandler" /> class.
    /// </summary>
    /// <param name="applications">The counter shared with the test.</param>
    public MutatingPaymentCommandHandler(ApplicationCounter applications)
    {
        _applications = applications;
    }

    /// <inheritdoc />
    public Task HandleAsync(MutatingPaymentCommand message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        message.PaymentId = string.Empty;
        _applications.Count++;
        return Task.CompletedTask;
    }
}
