using LiteBus.Transport.Kafka;

namespace LiteBus.Inbox.Ingress.Kafka;

/// <summary>
///     Configures Kafka inbox ingress and connection settings.
/// </summary>
public sealed record KafkaInboxIngressOptions
{
    /// <summary>
    ///     Gets the Kafka connection settings used by the ingress consumer.
    /// </summary>
    public required KafkaTransportOptions Connection { get; init; }

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
