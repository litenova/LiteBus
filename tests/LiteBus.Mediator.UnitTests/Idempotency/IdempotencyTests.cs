using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Runtime.Abstractions.Diagnostics;
using LiteBus.Runtime.Abstractions.Exceptions;
using LiteBus.Testing;
using LiteBus.Testing.Mediation;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Mediator.UnitTests.Idempotency;

/// <summary>
///     Verifies in-process idempotency: a declared key is claimed before the handler runs, settled after, and a repeat
///     is answered without running the work again.
/// </summary>
[Collection("Sequential")]
public sealed class IdempotencyTests : LiteBusTestBase
{
    /// <summary>
    ///     Builds a provider with idempotency enabled and the in-memory store registered.
    /// </summary>
    /// <param name="store">The store shared with the test.</param>
    /// <param name="applications">The counter recording how many times each handler actually ran.</param>
    /// <returns>The configured service provider.</returns>
    private static ServiceProvider BuildProvider(IIdempotencyStore store, ApplicationCounter applications)
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
                    builder.Register<ApplyPaymentCommand>();
                    builder.Register<ApplyPaymentCommandHandler>();
                    builder.Register<ApplyPaymentCommandDefinition>();
                    builder.Register<ReservePaymentCommand>();
                    builder.Register<ReservePaymentCommandHandler>();
                    builder.Register<ReservePaymentCommandDefinition>();
                    builder.Register<RepeatablePaymentCommand>();
                    builder.Register<RepeatablePaymentCommandHandler>();
                    builder.Register<RepeatablePaymentCommandDefinition>();
                    builder.Register<UndeclaredPaymentCommand>();
                    builder.Register<UndeclaredPaymentCommandHandler>();
                    builder.EnableIdempotency();
                });
            })
            .BuildServiceProvider();
    }

    [Fact]
    public async Task A_repeated_command_is_answered_without_running_again()
    {
        var applications = new ApplicationCounter();
        var provider = BuildProvider(new InMemoryIdempotencyStore(), applications);
        var mediator = provider.GetRequiredService<ICommandMediator>();

        await mediator.SendAsync(new ApplyPaymentCommand { PaymentId = "p-1" }).ConfigureAwait(false);
        await mediator.SendAsync(new ApplyPaymentCommand { PaymentId = "p-1" }).ConfigureAwait(false);

        applications.Count.Should().Be(1);
    }

    [Fact]
    public async Task A_different_key_runs_on_its_own()
    {
        var applications = new ApplicationCounter();
        var provider = BuildProvider(new InMemoryIdempotencyStore(), applications);
        var mediator = provider.GetRequiredService<ICommandMediator>();

        await mediator.SendAsync(new ApplyPaymentCommand { PaymentId = "p-1" }).ConfigureAwait(false);
        await mediator.SendAsync(new ApplyPaymentCommand { PaymentId = "p-2" }).ConfigureAwait(false);

        applications.Count.Should().Be(2);
    }

    [Fact]
    public async Task A_command_declaring_no_key_is_untouched()
    {
        var applications = new ApplicationCounter();
        var store = new InMemoryIdempotencyStore();
        var provider = BuildProvider(store, applications);
        var mediator = provider.GetRequiredService<ICommandMediator>();

        await mediator.SendAsync(new UndeclaredPaymentCommand()).ConfigureAwait(false);
        await mediator.SendAsync(new UndeclaredPaymentCommand()).ConfigureAwait(false);

        // One registration covers the axis and only the declaring commands pay for it.
        applications.Count.Should().Be(2);
        store.AppliedKeys.Should().BeEmpty();
    }

    [Fact]
    public async Task A_failed_command_releases_its_key_so_the_retry_runs()
    {
        var applications = new ApplicationCounter();
        var provider = BuildProvider(new InMemoryIdempotencyStore(), applications);
        var mediator = provider.GetRequiredService<ICommandMediator>();

        var act = async () => await mediator
            .SendAsync(new ApplyPaymentCommand { PaymentId = "p-3", ShouldThrow = true }).ConfigureAwait(false);

        await act.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(false);

        // Burning the key on a transient failure would turn the retry into a false repeat, which is the opposite of
        // what idempotency is for.
        await mediator.SendAsync(new ApplyPaymentCommand { PaymentId = "p-3" }).ConfigureAwait(false);

        applications.Count.Should().Be(2);
    }

    [Fact]
    public async Task The_key_is_scoped_so_two_message_types_do_not_collide()
    {
        var applications = new ApplicationCounter();
        var provider = BuildProvider(new InMemoryIdempotencyStore(), applications);
        var mediator = provider.GetRequiredService<ICommandMediator>();

        await mediator.SendAsync(new ApplyPaymentCommand { PaymentId = "shared" }).ConfigureAwait(false);
        await mediator.SendAsync(new ReservePaymentCommand { PaymentId = "shared" }).ConfigureAwait(false);

        applications.Count.Should().Be(2);
    }

    [Fact]
    public async Task A_repeat_replays_the_recorded_result_when_the_declaration_asks_for_it()
    {
        var applications = new ApplicationCounter();
        var provider = BuildProvider(new InMemoryIdempotencyStore(), applications);
        var mediator = provider.GetRequiredService<ICommandMediator>();

        var first = await mediator
            .SendAsync(new RepeatablePaymentCommand { PaymentId = "p-4" }).ConfigureAwait(false);

        var second = await mediator
            .SendAsync(new RepeatablePaymentCommand { PaymentId = "p-4" }).ConfigureAwait(false);

        applications.Count.Should().Be(1);
        second.Should().BeEquivalentTo(first);
    }

    [Fact]
    public async Task A_result_command_without_ReplayResult_names_the_fix_rather_than_inventing_an_answer()
    {
        var applications = new ApplicationCounter();
        var store = new InMemoryIdempotencyStore();

        var services = new ServiceCollection();
        services.AddSingleton<IIdempotencyStore>(store);
        services.AddSingleton(applications);

        var provider = services
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(_ => { });
                registry.AddCommands(builder =>
                {
                    builder.Register<SettlePaymentCommand>();
                    builder.Register<SettlePaymentCommandHandler>();
                    builder.Register<SettlePaymentCommandDefinition>();
                    builder.EnableIdempotency();
                });
            })
            .BuildServiceProvider();

        var mediator = provider.GetRequiredService<ICommandMediator>();

        await mediator.SendAsync(new SettlePaymentCommand { PaymentId = "p-5" }).ConfigureAwait(false);

        var act = async () => await mediator
            .SendAsync(new SettlePaymentCommand { PaymentId = "p-5" }).ConfigureAwait(false);

        // Returning default(TResult) would hand the caller a null that looks like a real answer.
        await act.Should().ThrowAsync<LiteBusConfigurationException>()
            .WithMessage("*ReplayResult*").ConfigureAwait(false);
    }

    [Fact]
    public async Task The_probe_reports_a_missing_store()
    {
        var services = new ServiceCollection();

        services.AddLiteBus(registry =>
        {
            registry.AddMessaging(_ => { });
            registry.AddCommands(builder =>
            {
                builder.Register<ApplyPaymentCommand>();
                builder.Register<ApplyPaymentCommandHandler>();
                builder.Register<ApplyPaymentCommandDefinition>();
                builder.EnableIdempotency();
            });
        });

        using var provider = services.BuildServiceProvider();

        var result = await new Messaging.Idempotency.IdempotencyStoreDiagnosticCheck(provider)
            .CheckAsync().ConfigureAwait(false);

        result.Status.Should().Be(DiagnosticStatus.Unhealthy);
        result.Data!["storeRegistered"].Should().Be(false);
    }

    [Fact]
    public async Task The_probe_reports_a_registered_store()
    {
        var provider = BuildProvider(new InMemoryIdempotencyStore(), new ApplicationCounter());

        var result = await new Messaging.Idempotency.IdempotencyStoreDiagnosticCheck(provider)
            .CheckAsync().ConfigureAwait(false);

        result.Status.Should().Be(DiagnosticStatus.Healthy);
        result.Data!["storeRegistered"].Should().Be(true);
        result.Data["storeType"].Should().Be(typeof(InMemoryIdempotencyStore).FullName);
    }

    [Fact]
    public async Task A_repeat_whose_store_recorded_no_payload_names_the_store_as_the_fix()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IIdempotencyStore>(new ForgetfulIdempotencyStore());
        services.AddSingleton(new ApplicationCounter());

        var provider = services
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(_ => { });
                registry.AddCommands(builder =>
                {
                    builder.Register<RepeatablePaymentCommand>();
                    builder.Register<RepeatablePaymentCommandHandler>();
                    builder.Register<RepeatablePaymentCommandDefinition>();
                    builder.EnableIdempotency();
                });
            })
            .BuildServiceProvider();

        // The declaration asked for a replay, so a store that reports a key applied without keeping its payload has
        // broken its half of the contract. Answering with default would look like a real receipt.
        var act = async () => await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new RepeatablePaymentCommand { PaymentId = "p-6" }).ConfigureAwait(false);

        await act.Should().ThrowAsync<LiteBusConfigurationException>()
            .WithMessage("*no recorded payload*").ConfigureAwait(false);
    }

    [Fact]
    public async Task A_blank_key_is_reported_rather_than_shared_by_every_message()
    {
        var applications = new ApplicationCounter();
        var provider = BuildProvider(new InMemoryIdempotencyStore(), applications);

        var act = async () => await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new ApplyPaymentCommand { PaymentId = "  " }).ConfigureAwait(false);

        // Every message with a blank key shares one key space, so the first would answer all the others.
        await act.Should().ThrowAsync<LiteBusConfigurationException>()
            .WithMessage("*blank key*").ConfigureAwait(false);
    }
}
