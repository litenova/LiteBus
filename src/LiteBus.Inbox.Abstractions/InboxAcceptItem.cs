using System;
using System.Diagnostics;
using LiteBus.Messaging.Abstractions.DurableMessaging;

namespace LiteBus.Inbox.Abstractions;

/// <summary>
///     Describes one message acceptance unit for batch writer calls.
/// </summary>
/// <remarks>
///     Use this shape when a single batch contains messages of different CLR types. Contract lookup uses
///     <see cref="MessageType" /> when supplied; otherwise <c>message.GetType()</c> is used for each entry.
/// </remarks>
[DebuggerDisplay("MessageType = {MessageType?.Name ?? Message.GetType().Name}")]
public sealed record InboxAcceptItem
{
    /// <summary>
    ///     Gets the message instance to serialize and store.
    /// </summary>
    public required object Message { get; init; }

    /// <summary>
    ///     Gets the optional runtime message type used for contract lookup on heterogeneous batches.
    /// </summary>
    public Type? MessageType { get; init; }

    /// <summary>
    ///     Gets per-message acceptance metadata applied outside the payload.
    /// </summary>
    public InboxAcceptMetadata Metadata { get; init; } = InboxAcceptMetadata.Immediate;

    /// <summary>
    ///     Creates an untyped acceptance item with optional metadata.
    /// </summary>
    /// <param name="message">The message instance to accept.</param>
    /// <param name="metadata">
    ///     Optional acceptance metadata. When omitted, <see cref="InboxAcceptMetadata.Immediate" /> is used.
    /// </param>
    /// <returns>An untyped acceptance item ready for batch APIs.</returns>
    public static InboxAcceptItem From(object message, InboxAcceptMetadata? metadata = null)
    {
        return new InboxAcceptItem
        {
            Message = message,
            Metadata = metadata ?? InboxAcceptMetadata.Immediate
        };
    }

    /// <summary>
    ///     Creates an untyped acceptance item with an explicit runtime type and optional metadata.
    /// </summary>
    /// <param name="message">The message instance to accept.</param>
    /// <param name="messageType">The runtime message type used for contract lookup.</param>
    /// <param name="metadata">
    ///     Optional acceptance metadata. When omitted, <see cref="InboxAcceptMetadata.Immediate" /> is used.
    /// </param>
    /// <returns>An untyped acceptance item ready for batch APIs.</returns>
    public static InboxAcceptItem From(object message, Type messageType, InboxAcceptMetadata? metadata = null)
    {
        return new InboxAcceptItem
        {
            Message = message,
            MessageType = messageType,
            Metadata = metadata ?? InboxAcceptMetadata.Immediate
        };
    }

    /// <summary>
    ///     Converts a typed acceptance item into an untyped batch entry.
    /// </summary>
    /// <typeparam name="TMessage">The compile-time message type.</typeparam>
    /// <param name="item">The typed acceptance item to convert.</param>
    /// <returns>An untyped acceptance item sharing the same message and metadata.</returns>
    public static InboxAcceptItem From<TMessage>(InboxAcceptItem<TMessage> item)
        where TMessage : notnull
    {
        return new InboxAcceptItem
        {
            Message = item.Message,
            MessageType = item.Message.GetType(),
            Metadata = item.Metadata
        };
    }
}
