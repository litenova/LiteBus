namespace LiteBus.Transport.InMemory.IntegrationTests;

/// <summary>
///     Integration event used by outbox transport dispatch tests.
/// </summary>
internal sealed record OrderSubmittedIntegrationEvent
{
    /// <summary>
    ///     Gets the order identifier carried by the event payload.
    /// </summary>
    public Guid OrderId { get; init; }
}
