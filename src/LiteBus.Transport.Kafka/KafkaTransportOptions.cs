namespace LiteBus.Transport.Kafka;

/// <summary>
///     Connection settings for Kafka transport adapters.
/// </summary>
public sealed class KafkaTransportOptions
{
    /// <summary>
    ///     Gets the bootstrap servers list passed to the Kafka client.
    /// </summary>
    public required string BootstrapServers { get; init; }

    /// <summary>
    ///     Gets the optional client identifier passed to producer and consumer clients.
    /// </summary>
    public string? ClientId { get; init; }

    /// <summary>
    ///     Gets the consumer group identifier used by <see cref="KafkaConsumer" />.
    /// </summary>
    public string ConsumerGroupId { get; init; } = "litebus-transport";

    /// <summary>
    ///     Gets the optional local message timeout in milliseconds passed to the Kafka producer.
    /// </summary>
    /// <value>
    ///     When <see langword="null" />, the Confluent producer default applies.
    /// </value>
    public int? MessageTimeoutMs { get; init; }

    /// <summary>
    ///     Gets the initial delay applied before re-consuming an offset that failed ingress processing.
    /// </summary>
    public TimeSpan SeekFailureBackoffInitial { get; init; } = TimeSpan.FromMilliseconds(250);

    /// <summary>
    ///     Gets the maximum delay applied before re-consuming a repeatedly failing offset.
    /// </summary>
    public TimeSpan SeekFailureBackoffMax { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    ///     Gets the multiplier applied to the seek failure backoff delay after each repeated failure at the same offset.
    /// </summary>
    public double SeekFailureBackoffMultiplier { get; init; } = 2.0;
}

