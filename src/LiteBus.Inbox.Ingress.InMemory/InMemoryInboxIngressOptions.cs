namespace LiteBus.Inbox.Ingress.InMemory;

/// <summary>
///     Configures in-memory inbox ingress settings.
/// </summary>
public sealed record InMemoryInboxIngressOptions
{
    /// <summary>
    ///     Gets the provider-neutral ingress safety settings.
    /// </summary>
    public TransportInboxIngressSafetyOptions Safety { get; init; } = new();

    /// <summary>
    ///     Gets the logical queue name the ingress consumer subscribes to.
    /// </summary>
    public string Destination { get; init; } = string.Empty;

    /// <summary>
    ///     Gets the maximum number of unacknowledged deliveries buffered by the channel.
    /// </summary>
    public ushort PrefetchCount { get; init; }

    /// <summary>
    ///     Gets a value indicating whether failed store writes should re-enqueue deliveries.
    /// </summary>
    public bool RequeueOnFailure { get; init; } = true;
}
