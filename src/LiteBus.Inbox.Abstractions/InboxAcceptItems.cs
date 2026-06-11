using System.Collections.Generic;

namespace LiteBus.Inbox.Abstractions;

/// <summary>
///     Provides factory helpers for constructing inbox acceptance items and batches.
/// </summary>
public static class InboxAcceptItems
{
    /// <summary>
    ///     Creates a typed acceptance item with optional metadata.
    /// </summary>
    /// <typeparam name="TMessage">The compile-time message type.</typeparam>
    /// <param name="message">The message instance to accept.</param>
    /// <param name="metadata">
    ///     Optional acceptance metadata. When omitted, <see cref="InboxAcceptMetadata.Default" /> is used.
    /// </param>
    /// <returns>A typed acceptance item ready for <see cref="IInbox.AcceptAsync{TMessage}" />.</returns>
    public static InboxAcceptItem<TMessage> From<TMessage>(TMessage message, InboxAcceptMetadata? metadata = null)
        where TMessage : notnull
    {
        return new InboxAcceptItem<TMessage>
        {
            Message = message,
            Metadata = metadata ?? InboxAcceptMetadata.Default
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

    /// <summary>
    ///     Creates an untyped acceptance item with optional metadata.
    /// </summary>
    /// <param name="message">The message instance to accept.</param>
    /// <param name="metadata">
    ///     Optional acceptance metadata. When omitted, <see cref="InboxAcceptMetadata.Default" /> is used.
    /// </param>
    /// <returns>An untyped acceptance item ready for batch APIs.</returns>
    public static InboxAcceptItem Untyped(object message, InboxAcceptMetadata? metadata = null)
    {
        return new InboxAcceptItem
        {
            Message = message,
            Metadata = metadata ?? InboxAcceptMetadata.Default
        };
    }

    /// <summary>
    ///     Builds a heterogeneous acceptance batch from pre-built untyped items.
    /// </summary>
    /// <param name="items">The acceptance items to include in the batch.</param>
    /// <returns>A read-only list suitable for <see cref="IInbox.AcceptBatchAsync" />.</returns>
    public static IReadOnlyList<InboxAcceptItem> From(params InboxAcceptItem[] items)
    {
        return items;
    }
}