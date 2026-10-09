using Confluent.Kafka;

namespace LiteBus.Transport.Kafka;

/// <summary>
///     Owns the shared Kafka producer and flushes it once at shutdown.
/// </summary>
/// <remarks>
///     Registered as a singleton by <see cref="KafkaTransportModule" />. <see cref="KafkaPublisher" /> resolves this
///     owner instead of owning the producer, so dispatch-scope teardown never flushes the shared producer.
/// </remarks>
internal sealed class KafkaProducerOwner : IDisposable
{
    /// <summary>
    ///     Gets the flush timeout applied when the container disposes the owner at shutdown.
    /// </summary>
    private static readonly TimeSpan FlushTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    ///     Gets the shared Kafka producer used to publish records.
    /// </summary>
    private readonly IProducer<string, byte[]> _producer;

    /// <summary>
    ///     Tracks whether disposal has started.
    /// </summary>
    private int _disposed;

    /// <summary>
    ///     Initializes a new instance of the <see cref="KafkaProducerOwner" /> class.
    /// </summary>
    /// <param name="options">The connection settings used to build the producer.</param>
    public KafkaProducerOwner(KafkaTransportOptions options)
        : this(BuildProducerConfig(options))
    {
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="KafkaProducerOwner" /> class from an explicit configuration.
    /// </summary>
    /// <param name="config">The producer configuration used to build the producer.</param>
    internal KafkaProducerOwner(ProducerConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        _producer = new ProducerBuilder<string, byte[]>(config).Build();
    }

    /// <summary>
    ///     Gets the shared Kafka producer used to publish records.
    /// </summary>
    /// <value>The producer built from the transport options.</value>
    public IProducer<string, byte[]> Producer => _producer;

    /// <summary>
    ///     Gets a value indicating whether the owner has been disposed.
    /// </summary>
    /// <value><see langword="true" /> after <see cref="Dispose" /> runs; otherwise, <see langword="false" />.</value>
    internal bool IsDisposed => Volatile.Read(ref _disposed) != 0;

    /// <summary>
    ///     Builds a producer configuration from transport options, setting each field only from its own option.
    /// </summary>
    /// <param name="options">The connection settings supplying producer tuning values.</param>
    /// <returns>The producer configuration ready for <see cref="ProducerBuilder{TKey, TValue}" />.</returns>
    internal static ProducerConfig BuildProducerConfig(KafkaTransportOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var config = new ProducerConfig
        {
            BootstrapServers = options.BootstrapServers,
            ClientId = options.ClientId,
            Acks = Acks.All,
        };

        if (options.MessageTimeoutMs is not null)
        {
            config.MessageTimeoutMs = options.MessageTimeoutMs.Value;
        }

        if (options.SocketTimeoutMs is not null)
        {
            config.SocketTimeoutMs = options.SocketTimeoutMs.Value;
        }

        if (options.MessageSendMaxRetries is not null)
        {
            config.MessageSendMaxRetries = options.MessageSendMaxRetries.Value;
        }

        return config;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        try
        {
            _producer.Flush(FlushTimeout);
        }
        finally
        {
            _producer.Dispose();
        }
    }
}
