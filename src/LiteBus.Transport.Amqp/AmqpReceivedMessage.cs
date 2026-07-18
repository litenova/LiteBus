using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Transport.Amqp;

/// <summary>
///     Represents one AMQP delivery received by a consumer.
/// </summary>
public sealed class AmqpReceivedMessage
{
    /// <summary>
    ///     Gets the delivery body. Copy or deserialize the payload before the handler returns.
    /// </summary>
    public required ReadOnlyMemory<byte> Body { get; init; }

    /// <summary>
    ///     Gets the application headers copied from the AMQP message.
    /// </summary>
    public required IReadOnlyDictionary<string, object?> Headers { get; init; }

    /// <summary>
    ///     Gets the broker delivery tag used for acknowledgement operations.
    /// </summary>
    public required ulong DeliveryTag { get; init; }

    /// <summary>
    ///     Gets the exchange the message was published to.
    /// </summary>
    public string? Exchange { get; init; }

    /// <summary>
    ///     Gets the routing key the message was published with.
    /// </summary>
    public string? RoutingKey { get; init; }

    /// <summary>
    ///     Gets the AMQP message identifier from message properties, when present.
    /// </summary>
    public string? MessageId { get; init; }

    /// <summary>
    ///     Gets the AMQP correlation identifier from message properties, when present.
    /// </summary>
    public string? CorrelationId { get; init; }

    /// <summary>
    ///     Gets a value indicating whether the broker previously attempted delivery.
    /// </summary>
    public bool Redelivered { get; init; }

    /// <summary>
    ///     Gets the delegate that acknowledges successful processing of the delivery.
    /// </summary>
    /// <remarks>
    ///     Prefer <see cref="AcceptAsync" />, <see cref="DiscardAsync" />, and <see cref="ReturnToQueueAsync" /> at call
    ///     sites.
    /// </remarks>
    public required Func<bool, CancellationToken, Task> AckDelegate { get; init; }

    /// <summary>
    ///     Gets the delegate that negative-acknowledges the delivery so the broker can requeue or dead-letter it.
    /// </summary>
    /// <remarks>
    ///     Prefer <see cref="AcceptAsync" />, <see cref="DiscardAsync" />, and <see cref="ReturnToQueueAsync" /> at call
    ///     sites.
    /// </remarks>
    public required Func<bool, bool, CancellationToken, Task> NackDelegate { get; init; }

    /// <summary>
    ///     Acknowledges the message, signalling the broker that processing succeeded.
    /// </summary>
    /// <param name="cancellationToken">A token that cancels the acknowledgement.</param>
    /// <returns>A task that completes when the broker has accepted the acknowledgement.</returns>
    public Task AcceptAsync(CancellationToken cancellationToken = default)
    {
        return AckDelegate(false, cancellationToken);
    }

    /// <summary>
    ///     Rejects the message and discards it from the queue.
    /// </summary>
    /// <remarks>
    ///     Use this when the message is malformed and will never succeed.
    /// </remarks>
    /// <param name="cancellationToken">A token that cancels the rejection.</param>
    /// <returns>A task that completes when the broker has accepted the rejection.</returns>
    public Task DiscardAsync(CancellationToken cancellationToken = default)
    {
        return NackDelegate(false, false, cancellationToken);
    }

    /// <summary>
    ///     Rejects the message and returns it to the queue for redelivery.
    /// </summary>
    /// <remarks>
    ///     Use this for transient failures where the message is expected to succeed on retry.
    /// </remarks>
    /// <param name="cancellationToken">A token that cancels the rejection.</param>
    /// <returns>A task that completes when the broker has accepted the rejection.</returns>
    public Task ReturnToQueueAsync(CancellationToken cancellationToken = default)
    {
        return NackDelegate(false, true, cancellationToken);
    }
}