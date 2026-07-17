namespace LiteBus.Transport.Abstractions;

/// <summary>
///     Configures one transport consumer subscription.
/// </summary>
public sealed record TransportConsumerOptions
{
    /// <summary>
    ///     Gets the default maximum number of handler callbacks that may execute concurrently.
    /// </summary>
    public const int DefaultMaxInFlightMessages = 32;

    /// <summary>
    ///     Gets the destination address to consume from such as an AMQP queue name.
    /// </summary>
    public required string Destination { get; init; }

    /// <summary>
    ///     Gets the optional subscription name used when the destination is a topic with named subscriptions.
    /// </summary>
    public string? SubscriptionName { get; init; }

    /// <summary>
    ///     Gets the maximum number of unacknowledged deliveries prefetched by brokers that support native prefetch.
    /// </summary>
    /// <remarks>
    ///     RabbitMQ and Azure Service Bus use this setting. SQS receive batching and callback concurrency use
    ///     <see cref="ReceiveBatchSize" /> and <see cref="MaxConcurrentCalls" /> instead.
    /// </remarks>
    public int PrefetchCount { get; init; }

    /// <summary>
    ///     Gets the number of messages requested by transports that receive a broker batch, such as Amazon SQS.
    /// </summary>
    public int ReceiveBatchSize { get; init; } = 1;

    /// <summary>
    ///     Gets the optional broker callback concurrency for transports that expose a native callback limit.
    /// </summary>
    /// <remarks>
    ///     Azure Service Bus uses this setting for <c>MaxConcurrentCalls</c>. It does not replace the provider-neutral
    ///     <see cref="MaxInFlightMessages" /> admission limit.
    /// </remarks>
    public int? MaxConcurrentCalls { get; init; }

    /// <summary>
    ///     Gets the maximum number of delivery handlers that LiteBus may execute concurrently for this subscription.
    /// </summary>
    /// <value>Default is <see cref="DefaultMaxInFlightMessages" />.</value>
    public int MaxInFlightMessages { get; init; } = DefaultMaxInFlightMessages;

    /// <summary>
    ///     Gets a value indicating whether the consumer should declare the destination before subscribing.
    /// </summary>
    public bool DeclareDestination { get; init; } = true;

    /// <summary>
    ///     Gets a value indicating whether the declared destination should survive broker restarts.
    /// </summary>
    public bool DurableDestination { get; init; } = true;

    /// <summary>
    ///     Gets a value indicating whether the consumer subscription is exclusive to this connection.
    /// </summary>
    public bool Exclusive { get; init; }

    /// <summary>
    ///     Gets the optional consumer tag assigned by the client.
    /// </summary>
    public string? ConsumerTag { get; init; }

    /// <summary>
    ///     Gets optional destination declaration arguments supplied to the broker.
    /// </summary>
    public IReadOnlyDictionary<string, object?>? DestinationArguments { get; init; }
}
