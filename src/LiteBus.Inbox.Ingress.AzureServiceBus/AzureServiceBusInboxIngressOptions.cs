namespace LiteBus.Inbox.Ingress.AzureServiceBus;

/// <summary>
///     Configures Azure Service Bus inbox ingress.
/// </summary>
public sealed record AzureServiceBusInboxIngressOptions
{
    /// <summary>
    ///     Gets the queue or topic name the ingress consumer subscribes to.
    /// </summary>
    public string Destination { get; init; } = string.Empty;

    /// <summary>
    ///     Gets the maximum number of unacknowledged deliveries prefetched by the processor.
    /// </summary>
    public ushort PrefetchCount { get; init; }

    /// <summary>
    ///     Gets a value indicating whether failed store writes should be abandoned for retry.
    /// </summary>
    public bool RequeueOnFailure { get; init; } = true;
}
