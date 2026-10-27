using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Mediator.UnitTests.Auditing;

/// <summary>
///     Verifies that target and reason selectors declared on the message reach the audit record, with the scope winning
///     when both supply a value.
/// </summary>
[Collection("Sequential")]
public sealed class AuditSelectorTests : LiteBusTestBase
{
    /// <summary>
    ///     Builds a provider with auditing enabled for the selector-declared commands.
    /// </summary>
    /// <param name="trail">The recording trail to register.</param>
    /// <returns>The configured service provider.</returns>
    private static ServiceProvider BuildProvider(RecordingAuditTrail trail)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IAuditTrail>(trail);

        return services
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(_ => { });

                registry.AddCommands(builder =>
                {
                    builder.Register<SelectorOrderCommand>();
                    builder.Register<SelectorOrderCommandHandler>();
                    builder.Register<SelectorOrderCommandDefinition>();
                    builder.Register<SelectorRefundCommand>();
                    builder.Register<SelectorRefundCommandHandler>();
                    builder.Register<SelectorRefundCommandDefinition>();
                    builder.Register<ThrowingSelectorCommand>();
                    builder.Register<ThrowingSelectorCommandHandler>();
                    builder.Register<ThrowingSelectorCommandDefinition>();
                    builder.Register<ThrowingSelectorWithScopeCommand>();
                    builder.Register<ThrowingSelectorWithScopeCommandHandler>();
                    builder.Register<ThrowingSelectorWithScopeCommandDefinition>();
                    builder.EnableAuditing();
                });
            })
            .BuildServiceProvider();
    }

    [Fact]
    public async Task Selectors_supply_the_target_and_reason_when_the_handler_pushes_nothing()
    {
        var trail = new RecordingAuditTrail();
        var provider = BuildProvider(trail);

        await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new SelectorOrderCommand { OrderId = "order-7", Justification = "restock" })
            .ConfigureAwait(false);

        var record = trail.Records.Should().ContainSingle().Subject;
        record.TargetKind.Should().Be("order");
        record.TargetId.Should().Be("order-7");
        record.Reason.Should().Be("restock");
    }

    [Fact]
    public async Task The_scope_wins_when_both_the_scope_and_a_selector_supply_a_value()
    {
        var trail = new RecordingAuditTrail();
        var provider = BuildProvider(trail);

        await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new SelectorOrderCommand { OrderId = "order-7", Justification = "restock", PushScope = true })
            .ConfigureAwait(false);

        // The handler knows what it did and the message only knows what it asked for, so the pushed values stand and
        // the selector results are discarded.
        var record = trail.Records.Should().ContainSingle().Subject;
        record.TargetId.Should().Be("order-from-scope");
        record.Reason.Should().Be("reason from scope");
    }

    [Fact]
    public async Task A_selector_is_never_invoked_when_the_scope_supplied_the_value()
    {
        var trail = new RecordingAuditTrail();
        var provider = BuildProvider(trail);

        await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new ThrowingSelectorWithScopeCommand())
            .ConfigureAwait(false);

        // The selector throws, so a record carrying the scope value proves the writer never invoked it.
        trail.Records.Should().ContainSingle().Which.TargetId.Should().Be("order-from-scope");
    }

    [Fact]
    public async Task A_null_or_blank_selector_result_leaves_the_value_empty()
    {
        var trail = new RecordingAuditTrail();
        var provider = BuildProvider(trail);

        await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new SelectorOrderCommand { Justification = "   " })
            .ConfigureAwait(false);

        var record = trail.Records.Should().ContainSingle().Subject;
        record.TargetId.Should().BeNull();
        record.Reason.Should().BeNull();
    }

    [Fact]
    public async Task A_required_reason_satisfied_by_a_selector_passes()
    {
        var trail = new RecordingAuditTrail();
        var provider = BuildProvider(trail);

        await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new SelectorRefundCommand { Justification = "duplicate charge" })
            .ConfigureAwait(false);

        trail.Records.Should().ContainSingle().Which.Reason.Should().Be("duplicate charge");
    }

    [Fact]
    public async Task A_required_reason_missing_from_the_selector_fails_like_a_missing_scope_reason()
    {
        var trail = new RecordingAuditTrail();
        var provider = BuildProvider(trail);

        var act = async () => await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new SelectorRefundCommand()).ConfigureAwait(false);

        // ReasonRequired applies to the final value, wherever it was meant to come from. A selector that yields
        // nothing is the same missing justification as a handler that pushed nothing.
        await act.Should().ThrowAsync<AuditReasonMissingException>().ConfigureAwait(false);

        trail.Records.Should().BeEmpty();
    }

    [Fact]
    public async Task A_throwing_selector_fails_the_record_write()
    {
        var trail = new RecordingAuditTrail();
        var provider = BuildProvider(trail);

        var act = async () => await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new ThrowingSelectorCommand()).ConfigureAwait(false);

        // A projection bug that silently produced evidence without its target would be worse than a failed mediation,
        // so the writer lets the selector exception fail the write the way any other writer failure does.
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("the projection is broken").ConfigureAwait(false);

        trail.Records.Should().BeEmpty();
    }
}

/// <summary>
///     A command whose target and reason are read off the message by selectors declared beside it.
/// </summary>
internal sealed class SelectorOrderCommand : ICommand
{
    /// <summary>
    ///     Gets or sets the order identifier the target selector reads.
    /// </summary>
    public string? OrderId { get; set; }

    /// <summary>
    ///     Gets or sets the justification the reason selector reads.
    /// </summary>
    public string? Justification { get; set; }

    /// <summary>
    ///     Gets or sets a value indicating whether the handler pushes scope values that override the selectors.
    /// </summary>
    public bool PushScope { get; set; }
}

/// <summary>
///     Declares the audit position of <see cref="SelectorOrderCommand" />, reading target and reason off the message.
/// </summary>
internal sealed class SelectorOrderCommandDefinition : IAuditDefinition<SelectorOrderCommand>
{
    /// <inheritdoc />
    public AuditDeclaration Audit => AuditDeclaration.Audited("orders.selector-order")
        .WithTargetFrom<SelectorOrderCommand>(command => command.OrderId)
        .WithReasonFrom<SelectorOrderCommand>(command => command.Justification)
        with
    { Category = "money", TargetKind = "order" };
}

/// <summary>
///     Pushes scope values only when the command asks it to, so one message proves both the selector path and the
///     scope override.
/// </summary>
internal sealed class SelectorOrderCommandHandler : ICommandHandler<SelectorOrderCommand>
{
    /// <summary>
    ///     Collects the handler-supplied audit detail.
    /// </summary>
    private readonly IAuditScope _audit;

    /// <summary>
    ///     Initializes a new instance of the <see cref="SelectorOrderCommandHandler" /> class.
    /// </summary>
    /// <param name="audit">The ambient audit scope.</param>
    public SelectorOrderCommandHandler(IAuditScope audit)
    {
        _audit = audit;
    }

    /// <inheritdoc />
    public Task HandleAsync(SelectorOrderCommand message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (message.PushScope)
        {
            _audit.WithTarget("order-from-scope").WithReason("reason from scope");
        }

        return Task.CompletedTask;
    }
}

/// <summary>
///     A command whose declaration requires a reason that its selector supplies.
/// </summary>
internal sealed class SelectorRefundCommand : ICommand
{
    /// <summary>
    ///     Gets or sets the justification the reason selector reads.
    /// </summary>
    public string? Justification { get; set; }
}

/// <summary>
///     Declares the audit position of <see cref="SelectorRefundCommand" />, requiring a reason read off the message.
/// </summary>
internal sealed class SelectorRefundCommandDefinition : IAuditDefinition<SelectorRefundCommand>
{
    /// <inheritdoc />
    public AuditDeclaration Audit => AuditDeclaration.Audited("orders.selector-refund")
        .WithReasonFrom<SelectorRefundCommand>(command => command.Justification)
        with
    { Category = "money", ReasonRequired = true };
}

/// <summary>
///     A handler for the command whose required reason comes from its selector.
/// </summary>
internal sealed class SelectorRefundCommandHandler : ICommandHandler<SelectorRefundCommand>
{
    /// <inheritdoc />
    public Task HandleAsync(SelectorRefundCommand message, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}

/// <summary>
///     A command whose target selector throws, to prove a projection bug fails the record write.
/// </summary>
internal sealed class ThrowingSelectorCommand : ICommand;

/// <summary>
///     Declares the audit position of <see cref="ThrowingSelectorCommand" /> with a broken target selector.
/// </summary>
internal sealed class ThrowingSelectorCommandDefinition : IAuditDefinition<ThrowingSelectorCommand>
{
    /// <inheritdoc />
    public AuditDeclaration Audit => AuditDeclaration.Audited("orders.throwing-selector")
        .WithTargetFrom<ThrowingSelectorCommand>(_ => throw new InvalidOperationException("the projection is broken"));
}

/// <summary>
///     A handler for the command whose target selector throws.
/// </summary>
internal sealed class ThrowingSelectorCommandHandler : ICommandHandler<ThrowingSelectorCommand>
{
    /// <inheritdoc />
    public Task HandleAsync(ThrowingSelectorCommand message, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}

/// <summary>
///     A command whose broken target selector must never run because its handler pushes the target through the scope.
/// </summary>
internal sealed class ThrowingSelectorWithScopeCommand : ICommand;

/// <summary>
///     Declares the audit position of <see cref="ThrowingSelectorWithScopeCommand" /> with a broken target selector.
/// </summary>
internal sealed class ThrowingSelectorWithScopeCommandDefinition : IAuditDefinition<ThrowingSelectorWithScopeCommand>
{
    /// <inheritdoc />
    public AuditDeclaration Audit => AuditDeclaration.Audited("orders.throwing-selector-with-scope")
        .WithTargetFrom<ThrowingSelectorWithScopeCommand>(_ => throw new InvalidOperationException("the projection is broken"));
}

/// <summary>
///     Pushes the target through the scope, so the broken selector must never run.
/// </summary>
internal sealed class ThrowingSelectorWithScopeCommandHandler : ICommandHandler<ThrowingSelectorWithScopeCommand>
{
    /// <summary>
    ///     Collects the handler-supplied audit detail.
    /// </summary>
    private readonly IAuditScope _audit;

    /// <summary>
    ///     Initializes a new instance of the <see cref="ThrowingSelectorWithScopeCommandHandler" /> class.
    /// </summary>
    /// <param name="audit">The ambient audit scope.</param>
    public ThrowingSelectorWithScopeCommandHandler(IAuditScope audit)
    {
        _audit = audit;
    }

    /// <inheritdoc />
    public Task HandleAsync(ThrowingSelectorWithScopeCommand message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        _audit.WithTarget("order-from-scope");
        return Task.CompletedTask;
    }
}
