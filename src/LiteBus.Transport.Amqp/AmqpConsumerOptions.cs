using System.Collections.Generic;
using LiteBus.Transport.Abstractions;

namespace LiteBus.Transport.Amqp;

/// <summary>
///     Consumer settings for an AMQP queue subscription.
/// </summary>
public sealed record AmqpConsumerOptions
{
    /// <summary>
    ///     Gets the queue name to consume from.
    /// </summary>
    public required string QueueName { get; init; }

    /// <summary>
    ///     Gets the maximum number of unacknowledged deliveries prefetched by the broker.
    /// </summary>
    /// <value>Use zero for RabbitMQ's unlimited native prefetch window.</value>
    public ushort PrefetchCount { get; init; } = 1;

    /// <summary>
    ///     Gets the maximum number of delivery handlers that LiteBus may execute concurrently for this subscription.
    /// </summary>
    /// <value>Default is <see cref="TransportConsumerOptions.DefaultMaxInFlightMessages" />.</value>
    public int MaxInFlightMessages { get; init; } = TransportConsumerOptions.DefaultMaxInFlightMessages;

    /// <summary>
    ///     Gets a value indicating whether the consumer should be exclusive to this connection.
    /// </summary>
    public bool Exclusive { get; init; }

    /// <summary>
    ///     Gets the optional consumer tag supplied to the broker.
    /// </summary>
    public string? ConsumerTag { get; init; }

    /// <summary>
    ///     Gets a value indicating whether the consumer should declare the queue before subscribing.
    /// </summary>
    public bool DeclareQueue { get; init; } = true;

    /// <summary>
    ///     Gets a value indicating whether a declared queue should survive broker restarts.
    /// </summary>
    public bool DurableQueue { get; init; } = true;

    /// <summary>
    ///     Gets the optional queue arguments passed to <c>queue.declare</c>.
    /// </summary>
    public IReadOnlyDictionary<string, object?>? QueueArguments { get; init; }
}
