using System;
using LiteBus.Messaging.Abstractions.DurableMessaging;

namespace LiteBus.Inbox.Abstractions;

/// <summary>
///     Describes one message acceptance unit for typed writer calls.
/// </summary>
/// <typeparam name="TMessage">The compile-time message type carried in the item.</typeparam>
/// <remarks>
///     Contract lookup at runtime always uses <c>message.GetType()</c> for each instance. Use
///     <see cref="InboxAcceptItem" /> when building heterogeneous batches.
/// </remarks>
public sealed record InboxAcceptItem<TMessage>
    where TMessage : notnull
{
    /// <summary>
    ///     Gets the message instance to serialize and store.
    /// </summary>
    public required TMessage Message { get; init; }

    /// <summary>
    ///     Gets per-message acceptance metadata applied outside the payload.
    /// </summary>
    public InboxAcceptMetadata Metadata { get; init; } = InboxAcceptMetadata.Immediate;

    /// <summary>
    ///     Creates a typed acceptance item with optional metadata.
    /// </summary>
    /// <param name="message">The message instance to accept.</param>
    /// <param name="metadata">
    ///     Optional acceptance metadata. When omitted, <see cref="InboxAcceptMetadata.Immediate" /> is used.
    /// </param>
    /// <returns>A typed acceptance item ready for <see cref="IInbox.AcceptAsync{TMessage}(InboxAcceptItem{TMessage}, System.Threading.CancellationToken)" />.</returns>
    public static InboxAcceptItem<TMessage> From(TMessage message, InboxAcceptMetadata? metadata = null)
    {
        return new InboxAcceptItem<TMessage>
        {
            Message = message,
            Metadata = metadata ?? InboxAcceptMetadata.Immediate
        };
    }

    /// <summary>
    ///     Creates an acceptance item that defers processor leasing until the specified UTC timestamp.
    /// </summary>
    /// <param name="message">The message instance to accept.</param>
    /// <param name="visibleAfter">The earliest UTC timestamp at which the message may be leased.</param>
    /// <returns>An acceptance item with <see cref="MessageVisibility.At" /> visibility metadata.</returns>
    public static InboxAcceptItem<TMessage> ScheduledAt(TMessage message, DateTimeOffset visibleAfter)
    {
        return From(message) with
        {
            Metadata = InboxAcceptMetadata.Immediate with
            {
                Visibility = new MessageVisibility.At(visibleAfter)
            }
        };
    }

    /// <summary>
    ///     Creates an acceptance item that defers processor leasing until a relative delay elapses.
    /// </summary>
    /// <param name="message">The message instance to accept.</param>
    /// <param name="delay">The non-negative delay before the message becomes visible.</param>
    /// <returns>An acceptance item with <see cref="MessageVisibility.After" /> visibility metadata.</returns>
    public static InboxAcceptItem<TMessage> ScheduledAfter(TMessage message, TimeSpan delay)
    {
        return From(message) with
        {
            Metadata = InboxAcceptMetadata.Immediate with
            {
                Visibility = new MessageVisibility.After(delay)
            }
        };
    }

    /// <summary>
    ///     Creates an acceptance item that stores an application-defined idempotency key with the envelope.
    /// </summary>
    /// <param name="message">The message instance to accept.</param>
    /// <param name="idempotencyKey">The idempotency key used for insert-time deduplication.</param>
    /// <returns>An acceptance item with <see cref="Idempotency.Keyed" /> metadata.</returns>
    public static InboxAcceptItem<TMessage> WithIdempotency(TMessage message, string idempotencyKey)
    {
        return From(message) with
        {
            Metadata = InboxAcceptMetadata.Immediate with
            {
                Idempotency = new Idempotency.Keyed(idempotencyKey)
            }
        };
    }

    /// <summary>
    ///     Creates an acceptance item that stores a caller-supplied inbox message identifier.
    /// </summary>
    /// <param name="message">The message instance to accept.</param>
    /// <param name="messageId">The message identifier supplied by the caller.</param>
    /// <returns>An acceptance item with <see cref="MessageIdentity.Supplied" /> metadata.</returns>
    public static InboxAcceptItem<TMessage> WithIdentity(TMessage message, Guid messageId)
    {
        return From(message) with
        {
            Metadata = InboxAcceptMetadata.Immediate with
            {
                Identity = new MessageIdentity.Supplied(messageId)
            }
        };
    }
}

/// <summary>
///     Describes one message acceptance unit for batch writer calls.
/// </summary>
/// <remarks>
///     Use this shape when a single batch contains messages of different CLR types. Contract lookup uses
///     <c>message.GetType()</c> for each entry.
/// </remarks>
public sealed record InboxAcceptItem
{
    /// <summary>
    ///     Gets the message instance to serialize and store.
    /// </summary>
    public required object Message { get; init; }

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
            Metadata = item.Metadata
        };
    }
}
