namespace LiteBus.Transport.Abstractions;

/// <summary>
///     Configures one transport consumer subscription.
/// </summary>
public sealed record TransportConsumerOptions
{
    /// <summary>
    ///     Gets the destination address to consume from such as an AMQP queue name.
    /// </summary>
    public required string Destination { get; init; }

    /// <summary>
    ///     Gets the optional subscription name used when the destination is a topic with named subscriptions.
    /// </summary>
    public string? SubscriptionName { get; init; }

    /// <summary>
    ///     Gets the maximum number of unacknowledged deliveries the broker should push to the consumer.
    /// </summary>
    public ushort PrefetchCount { get; init; }

    /// <summary>
    ///     Gets the maximum number of handler callbacks that may execute concurrently when the broker supports a separate limit.
    /// </summary>
    public ushort? MaxConcurrentMessages { get; init; }

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
