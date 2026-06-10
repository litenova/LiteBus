namespace LiteBus.DurableTransport.IntegrationTesting;

/// <summary>
///     Command payload used by inbox dispatch integration scenarios.
/// </summary>
public sealed record RemoteWorkCommand
{
    /// <summary>
    ///     Gets the work item identifier carried by the command.
    /// </summary>
    public Guid WorkItemId { get; init; }

    /// <summary>
    ///     Gets the idempotency key stored with the inbox envelope.
    /// </summary>
    public string IdempotencyKey { get; init; } = string.Empty;
}

/// <summary>
///     Command payload used by inbox ingress end-to-end scenarios.
/// </summary>
public sealed record ShipOrderCommand
{
    /// <summary>
    ///     Gets the order identifier carried by the command.
    /// </summary>
    public Guid OrderId { get; init; }
}

/// <summary>
///     Integration event payload used by outbox dispatch scenarios.
/// </summary>
public sealed class OrderSubmittedIntegrationEvent
{
    /// <summary>
    ///     Gets the order identifier carried by the event.
    /// </summary>
    public Guid OrderId { get; init; }
}
