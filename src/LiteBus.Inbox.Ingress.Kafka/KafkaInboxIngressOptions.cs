namespace LiteBus.Inbox.Ingress.Kafka;

/// <summary>
///     Configures Kafka inbox ingress.
/// </summary>
public sealed record KafkaInboxIngressOptions
{
    /// <summary>
    ///     Gets the provider-neutral ingress safety settings.
    /// </summary>
    public TransportInboxIngressSafetyOptions Safety { get; init; } = new();

    /// <summary>
    ///     Gets the topic name the ingress consumer subscribes to.
    /// </summary>
    public string Destination { get; init; } = string.Empty;

    /// <summary>
    ///     Gets the maximum number of records prefetched per consume loop iteration.
    /// </summary>
    public ushort PrefetchCount { get; init; }

    /// <summary>
    ///     Gets a value indicating whether failed store writes should leave offsets uncommitted for retry.
    /// </summary>
    public bool RequeueOnFailure { get; init; } = true;
}
