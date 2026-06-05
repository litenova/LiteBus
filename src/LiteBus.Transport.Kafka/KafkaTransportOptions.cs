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
}

