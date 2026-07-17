namespace LiteBus.Inbox.Ingress.AzureServiceBus;

/// <summary>
///     Configures Azure Service Bus inbox ingress.
/// </summary>
public sealed record AzureServiceBusInboxIngressOptions
{
    /// <summary>
    ///     Gets the provider-neutral ingress safety settings.
    /// </summary>
    public TransportInboxIngressSafetyOptions Safety { get; init; } = new();

    /// <summary>
    ///     Gets the queue or topic name the ingress consumer subscribes to.
    /// </summary>
    public string Destination { get; init; } = string.Empty;

    /// <summary>
    ///     Gets the subscription name when <see cref="Destination" /> identifies a topic.
    /// </summary>
    public string? SubscriptionName { get; init; }

    /// <summary>
    ///     Gets the maximum number of unacknowledged deliveries prefetched by the processor.
    /// </summary>
    public ushort PrefetchCount { get; init; }

    /// <summary>
    ///     Gets the maximum number of Azure Service Bus callbacks that may execute concurrently.
    /// </summary>
    public ushort? MaxConcurrentMessages { get; init; }

    /// <summary>
    ///     Gets a value indicating whether failed store writes should be abandoned for retry.
    /// </summary>
    public bool RequeueOnFailure { get; init; } = true;
}
