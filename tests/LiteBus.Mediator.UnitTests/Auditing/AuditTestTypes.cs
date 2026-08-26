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
///     A command whose audit position is declared by a definition facet.
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
///     A custom facet declared outside LiteBus, proving the registry applies facets it knows nothing about.
/// </summary>
/// <param name="Name">The permission the use case requires.</param>
internal sealed record RequiredPermission(string Name);

/// <summary>
///     Declares the permission a message requires, using an application-owned metadata value.
/// </summary>
/// <typeparam name="TMessage">The message type this facet describes.</typeparam>
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

        _audit.Target("order-42").WithReason("customer requested").WithProperty("channel", "web");
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
