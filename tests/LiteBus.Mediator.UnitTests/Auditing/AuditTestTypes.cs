using System.Collections.Concurrent;
using LiteBus.Commands.Abstractions;
using LiteBus.Messaging.Abstractions;
using LiteBus.Queries.Abstractions;

namespace LiteBus.Mediator.UnitTests.Auditing;

/// <summary>
///     An in-memory audit trail that records everything written to it.
/// </summary>
internal sealed class RecordingAuditTrail : IAuditTrail
{
    /// <summary>
    ///     Gets the records written during the test.
    /// </summary>
    public ConcurrentQueue<AuditRecord> Records { get; } = new();

    /// <inheritdoc />
    public Task WriteAsync(AuditRecord record, CancellationToken cancellationToken)
    {
        // A real trail honours its token. Doing the same here is what makes the cancellation test meaningful: handing
        // this trail the token that just fired would lose exactly the record a review would look for.
        cancellationToken.ThrowIfCancellationRequested();
        Records.Enqueue(record);
        return Task.CompletedTask;
    }
}

/// <summary>
///     Raised by a handler to represent a refusal, so a custom outcome mapper can record it as a denial.
/// </summary>
internal sealed class ForbiddenException : Exception
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="ForbiddenException" /> class.
    /// </summary>
    public ForbiddenException() : base("forbidden")
    {
    }
}

/// <summary>
///     Records a refusal exception as a denial rather than a failure.
/// </summary>
internal sealed class TestAuditOutcomeMapper : IAuditOutcomeMapper
{
    /// <inheritdoc />
    public AuditOutcome Map(MessageCompletionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.Exception is ForbiddenException
            ? AuditOutcome.Denied
            : LiteBus.Messaging.Audit.DefaultAuditOutcomeMapper.MapByOutcome(context);
    }
}

/// <summary>
///     A command whose audit position is declared by attribute.
/// </summary>
[Audited("orders.place-order", Category = "money", TargetKind = "order")]
internal sealed class PlaceOrderCommand : ICommand
{
    /// <summary>
    ///     Gets or sets a value indicating whether the handler refuses the action.
    /// </summary>
    public bool ShouldRefuse { get; set; }
}

/// <summary>
///     A command whose audit position is declared by a definition.
/// </summary>
internal sealed class ShipOrderCommand : ICommand;

/// <summary>
///     A command deliberately excluded from the audit trail.
/// </summary>
[AuditExempt("browsing a public storefront is not a sensitive action")]
internal sealed class BrowseStorefrontCommand : ICommand;

/// <summary>
///     A command that carries an attribute and a definition, to prove the definition wins.
/// </summary>
[Audited("orders.cancel-order-from-attribute")]
internal sealed class CancelOrderCommand : ICommand;

/// <summary>
///     A query whose reads are audited.
/// </summary>
[Audited("orders.export-orders", Category = "privacy")]
internal sealed class ExportOrdersQuery : IQuery<string>;

/// <summary>
///     Declares the audit position of <see cref="ShipOrderCommand" /> beside the command.
/// </summary>
internal sealed class ShipOrderCommandDefinition : IAuditDefinition<ShipOrderCommand>
{
    /// <inheritdoc />
    public AuditDeclaration Audit => AuditDeclaration.Audited("orders.ship-order") with
    {
        Category = "lifecycle",
        TargetKind = "shipment"
    };
}

/// <summary>
///     Overrides the attribute declaration on <see cref="CancelOrderCommand" />.
/// </summary>
internal sealed class CancelOrderCommandDefinition : IAuditDefinition<CancelOrderCommand>
{
    /// <inheritdoc />
    public AuditDeclaration Audit => AuditDeclaration.Audited("orders.cancel-order-from-definition");
}

/// <summary>
///     A declaration type owned outside LiteBus, proving the registry applies values it knows nothing about.
/// </summary>
/// <param name="Name">The permission the use case requires.</param>
internal sealed record RequiredPermission(string Name);

/// <summary>
///     Declares the permission a message requires, using an application-owned metadata value.
/// </summary>
/// <typeparam name="TMessage">The message type this definition describes.</typeparam>
internal interface IPermissionDefinition<TMessage> : IMessageDefinition<TMessage, RequiredPermission>
    where TMessage : notnull
{
    /// <summary>
    ///     Gets the permission the message requires.
    /// </summary>
    RequiredPermission Required { get; }

    /// <inheritdoc />
    RequiredPermission IMessageDefinition<TMessage, RequiredPermission>.Value => Required;
}

/// <summary>
///     Declares both an audit position and an application-owned permission for one command.
/// </summary>
internal sealed class PlaceOrderCommandDefinition : IPermissionDefinition<PlaceOrderCommand>
{
    /// <inheritdoc />
    public RequiredPermission Required => new("orders.place");
}

/// <summary>
///     Sets a target identifier the way a real handler would, from a value it generates itself.
/// </summary>
internal sealed class PlaceOrderCommandHandler : ICommandHandler<PlaceOrderCommand>
{
    /// <summary>
    ///     The audit scope used to push the generated identifier.
    /// </summary>
    private readonly IAuditScope _audit;

    /// <summary>
    ///     Initializes a new instance of the <see cref="PlaceOrderCommandHandler" /> class.
    /// </summary>
    /// <param name="audit">The ambient audit scope.</param>
    public PlaceOrderCommandHandler(IAuditScope audit)
    {
        _audit = audit;
    }

    /// <inheritdoc />
    public Task HandleAsync(PlaceOrderCommand message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (message.ShouldRefuse)
        {
            throw new ForbiddenException();
        }

        _audit.WithTarget("order-42").WithReason("customer requested").WithProperty("channel", "web");
        return Task.CompletedTask;
    }
}

/// <summary>
///     A handler for the definition-declared command.
/// </summary>
internal sealed class ShipOrderCommandHandler : ICommandHandler<ShipOrderCommand>
{
    /// <inheritdoc />
    public Task HandleAsync(ShipOrderCommand message, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}

/// <summary>
///     A handler for the exempt command.
/// </summary>
internal sealed class BrowseStorefrontCommandHandler : ICommandHandler<BrowseStorefrontCommand>
{
    /// <inheritdoc />
    public Task HandleAsync(BrowseStorefrontCommand message, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}

/// <summary>
///     A handler for the command that carries both an attribute and a definition.
/// </summary>
internal sealed class CancelOrderCommandHandler : ICommandHandler<CancelOrderCommand>
{
    /// <inheritdoc />
    public Task HandleAsync(CancelOrderCommand message, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}

/// <summary>
///     A handler for the audited query.
/// </summary>
internal sealed class ExportOrdersQueryHandler : IQueryHandler<ExportOrdersQuery, string>
{
    /// <inheritdoc />
    public Task<string> HandleAsync(ExportOrdersQuery message, CancellationToken cancellationToken = default)
    {
        return Task.FromResult("csv");
    }
}

/// <summary>
///     A marker for writes that share one audit position, so a single definition can cover a family of commands.
/// </summary>
internal interface IAuditableWrite : ICommand;

/// <summary>
///     Declares the audit position of every <see cref="IAuditableWrite" />, without naming a concrete command.
/// </summary>
internal sealed class AuditableWriteDefinition : IAuditDefinition<IAuditableWrite>
{
    /// <inheritdoc />
    public AuditDeclaration Audit => AuditDeclaration.Audited("writes.generic") with { Category = "lifecycle" };
}

/// <summary>
///     A command covered by the marker definition rather than by one of its own.
/// </summary>
internal sealed class AdjustStockCommand : IAuditableWrite;

/// <summary>
///     A handler for the command covered by the marker definition.
/// </summary>
internal sealed class AdjustStockCommandHandler : ICommandHandler<AdjustStockCommand>
{
    /// <inheritdoc />
    public Task HandleAsync(AdjustStockCommand message, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}

/// <summary>
///     A command whose declaration requires the handler to justify the action.
/// </summary>
[Audited("orders.override-price", ReasonRequired = true)]
internal sealed class OverridePriceCommand : ICommand
{
    /// <summary>
    ///     Gets or sets a value indicating whether the handler supplies the required reason.
    /// </summary>
    public bool SupplyReason { get; set; }
}

/// <summary>
///     Supplies the required reason only when the command asks it to.
/// </summary>
internal sealed class OverridePriceCommandHandler : ICommandHandler<OverridePriceCommand>
{
    /// <summary>
    ///     Collects the handler-supplied audit detail.
    /// </summary>
    private readonly IAuditScope _audit;

    /// <summary>
    ///     Initializes a new instance of the <see cref="OverridePriceCommandHandler" /> class.
    /// </summary>
    /// <param name="audit">The ambient audit scope.</param>
    public OverridePriceCommandHandler(IAuditScope audit)
    {
        _audit = audit;
    }

    /// <inheritdoc />
    public Task HandleAsync(OverridePriceCommand message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (message.SupplyReason)
        {
            _audit.WithReason("manager approved");
        }

        return Task.CompletedTask;
    }
}

/// <summary>
///     A command whose gate refuses it, so the trail records a denial without an outcome mapper.
/// </summary>
[Audited("orders.approve-refund", Category = "money")]
internal sealed class ApproveRefundCommand : ICommand
{
    /// <summary>
    ///     Gets or sets a value indicating whether the gate refuses the command.
    /// </summary>
    public bool ShouldDeny { get; set; }

    /// <summary>
    ///     Gets or sets a value indicating whether the mediation is cancelled by the handler.
    /// </summary>
    public bool ShouldCancel { get; set; }
}

/// <summary>
///     Refuses the refund when the command asks for it.
/// </summary>
internal sealed class ApproveRefundCommandGuard : ICommandGuard<ApproveRefundCommand>
{
    /// <inheritdoc />
    public Task<Verdict> CheckAsync(
        ApproveRefundCommand message,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        return Task.FromResult(message.ShouldDeny
            ? Verdict.Deny("the approver is the requester")
            : Verdict.Allow);
    }
}

/// <summary>
///     Completes, or observes the caller's cancellation.
/// </summary>
internal sealed class ApproveRefundCommandHandler : ICommandHandler<ApproveRefundCommand>
{
    /// <inheritdoc />
    public Task HandleAsync(ApproveRefundCommand message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (message.ShouldCancel)
        {
            cancellationToken.ThrowIfCancellationRequested();
        }

        return Task.CompletedTask;
    }
}

/// <summary>
///     A query answered from cache by its shortcut, to prove an early answer is not recorded as a denial.
/// </summary>
[Audited("orders.read-order", Category = "privacy")]
internal sealed class ReadOrderQuery : IQuery<string>
{
    /// <summary>
    ///     Gets or sets a value indicating whether the shortcut answers without the handler.
    /// </summary>
    public bool ServeFromCache { get; set; }
}

/// <summary>
///     Answers the query from cache when asked, the way a real cache would.
/// </summary>
internal sealed class ReadOrderQueryShortcut : IQueryShortcut<ReadOrderQuery, string>
{
    /// <inheritdoc />
    public Task<Shortcut<string>> TryAnswerAsync(
        ReadOrderQuery message,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        return Task.FromResult(message.ServeFromCache
            ? Shortcut<string>.Answer("cached-order", "served from cache")
            : Shortcut<string>.None);
    }
}

/// <summary>
///     Reads the order when no shortcut answers the query.
/// </summary>
internal sealed class ReadOrderQueryHandler : IQueryHandler<ReadOrderQuery, string>
{
    /// <inheritdoc />
    public Task<string> HandleAsync(ReadOrderQuery message, CancellationToken cancellationToken = default)
    {
        return Task.FromResult("stored-order");
    }
}

/// <summary>
///     A command declared twice, to prove a duplicate declaration is reported instead of one silently winning.
/// </summary>
internal sealed class DoubleDeclaredCommand : ICommand;

/// <summary>
///     The first declaration for <see cref="DoubleDeclaredCommand" />.
/// </summary>
internal sealed class FirstDoubleDeclaration : IAuditDefinition<DoubleDeclaredCommand>
{
    /// <inheritdoc />
    public AuditDeclaration Audit => AuditDeclaration.Audited("orders.first");
}

/// <summary>
///     The second declaration for <see cref="DoubleDeclaredCommand" />.
/// </summary>
internal sealed class SecondDoubleDeclaration : IAuditDefinition<DoubleDeclaredCommand>
{
    /// <inheritdoc />
    public AuditDeclaration Audit => AuditDeclaration.Audited("orders.second");
}
