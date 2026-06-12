using System;
using System.Diagnostics;
using LiteBus.Messaging.Abstractions.DurableMessaging;

namespace LiteBus.Outbox.Abstractions;

/// <summary>
///     Represents one event enqueue command with explicit runtime type information for contract lookup.
/// </summary>
/// <remarks>
///     Use this shape for heterogeneous batch enqueues where each message may resolve to a different contract.
///     Contract lookup always uses <see cref="MessageType" /> rather than only the compile-time generic argument.
/// </remarks>
[DebuggerDisplay("MessageType = {MessageType?.Name ?? Message.GetType().Name}")]
public sealed record OutboxEnqueueItem
{
    /// <summary>
    ///     Gets the message instance to serialize and store.
    /// </summary>
    public required object Message { get; init; }

    /// <summary>
    ///     Gets the runtime message type used for contract lookup.
    /// </summary>
    public required Type MessageType { get; init; }

    /// <summary>
    ///     Gets the durable metadata applied when the message is enqueued.
    /// </summary>
    public OutboxEnqueueMetadata Metadata { get; init; } = OutboxEnqueueMetadata.Immediate;

    /// <summary>
    ///     Creates an enqueue item for one event using the runtime message type and default metadata.
    /// </summary>
    /// <param name="message">The message instance to serialize and store.</param>
    /// <returns>An enqueue item that uses <see cref="OutboxEnqueueMetadata.Immediate" />.</returns>
    public static OutboxEnqueueItem From(object message)
    {
        ArgumentNullException.ThrowIfNull(message);

        return new OutboxEnqueueItem
        {
            Message = message,
            MessageType = message.GetType()
        };
    }

    /// <summary>
    ///     Creates an enqueue item for one event with an explicit runtime type and default metadata.
    /// </summary>
    /// <param name="message">The message instance to serialize and store.</param>
    /// <param name="messageType">The runtime message type used for contract lookup.</param>
    /// <returns>An enqueue item that uses <see cref="OutboxEnqueueMetadata.Immediate" />.</returns>
    public static OutboxEnqueueItem From(object message, Type messageType)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(messageType);

        return new OutboxEnqueueItem { Message = message, MessageType = messageType };
    }

    /// <summary>
    ///     Creates an enqueue item for one event with an explicit runtime type and caller-supplied metadata.
    /// </summary>
    /// <param name="message">The message instance to serialize and store.</param>
    /// <param name="messageType">The runtime message type used for contract lookup.</param>
    /// <param name="metadata">The durable metadata applied when the event is enqueued.</param>
    /// <returns>An enqueue item carrying the supplied metadata.</returns>
    public static OutboxEnqueueItem From(object message, Type messageType, OutboxEnqueueMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(messageType);

        return new OutboxEnqueueItem { Message = message, MessageType = messageType, Metadata = metadata };
    }
}
