using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Transport.Abstractions;

/// <summary>
///     Represents one inbound transport delivery received by a consumer.
/// </summary>
public sealed class TransportMessage
{
    /// <summary>
    ///     Gets the delivery body. Copy or deserialize the payload before the handler returns.
    /// </summary>
    public required ReadOnlyMemory<byte> Body { get; init; }

    /// <summary>
    ///     Gets the application headers copied from the transport message.
    /// </summary>
    public required IReadOnlyDictionary<string, object?> Headers { get; init; }

    /// <summary>
    ///     Gets the primary destination address the message was published to, when available.
    /// </summary>
    public string? Destination { get; init; }

    /// <summary>
    ///     Gets the route within the destination, when available.
    /// </summary>
    public string? Route { get; init; }

    /// <summary>
    ///     Gets the transport message identifier from message properties, when present.
    /// </summary>
    public string? MessageId { get; init; }

    /// <summary>
    ///     Gets the correlation identifier from message properties, when present.
    /// </summary>
    public string? CorrelationId { get; init; }

    /// <summary>
    ///     Gets a value indicating whether the broker previously attempted delivery.
    /// </summary>
    public bool Redelivered { get; init; }

    /// <summary>
    ///     Gets the delegate that acknowledges successful processing of the delivery.
    /// </summary>
    public required Func<CancellationToken, Task> AckAsync { get; init; }

    /// <summary>
    ///     Gets the delegate that negative-acknowledges the delivery.
    /// </summary>
    /// <remarks>
    ///     The delegate receives a requeue flag. When <see langword="true" />, the broker should return the message
    ///     for redelivery; otherwise the message is discarded or dead-lettered.
    /// </remarks>
    public required Func<bool, CancellationToken, Task> NackAsync { get; init; }

    /// <summary>
    ///     Acknowledges the message, signalling the broker that processing succeeded.
    /// </summary>
    /// <param name="cancellationToken">A token that cancels the acknowledgement.</param>
    /// <returns>A task that completes when the broker has accepted the acknowledgement.</returns>
    public Task AcceptAsync(CancellationToken cancellationToken = default) =>
        AckAsync(cancellationToken);

    /// <summary>
    ///     Rejects the message and discards it from the queue.
    /// </summary>
    /// <param name="cancellationToken">A token that cancels the rejection.</param>
    /// <returns>A task that completes when the broker has accepted the rejection.</returns>
    public Task DiscardAsync(CancellationToken cancellationToken = default) =>
        NackAsync(false, cancellationToken);

    /// <summary>
    ///     Rejects the message and returns it to the queue for redelivery.
    /// </summary>
    /// <param name="cancellationToken">A token that cancels the rejection.</param>
    /// <returns>A task that completes when the broker has accepted the rejection.</returns>
    public Task ReturnToQueueAsync(CancellationToken cancellationToken = default) =>
        NackAsync(true, cancellationToken);
}
