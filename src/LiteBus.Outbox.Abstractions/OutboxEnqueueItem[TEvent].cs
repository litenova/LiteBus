using System;
using System.Diagnostics;
using LiteBus.Messaging.Abstractions.DurableMessaging;

namespace LiteBus.Outbox.Abstractions;

/// <summary>
///     Represents one typed event enqueue command.
/// </summary>
/// <remarks>
///     Contract lookup uses <c>message.GetType()</c> for each instance. The compile-time type parameter documents caller
///     intent and enables typed receipts without a separate runtime type argument.
/// </remarks>
/// <typeparam name="TEvent">The compile-time event type associated with the enqueue command.</typeparam>
[DebuggerDisplay("MessageType = {typeof(TEvent).Name}")]
public sealed record OutboxEnqueueItem<TEvent>
    where TEvent : notnull
{
    /// <summary>
    ///     Gets the message instance to serialize and store.
    /// </summary>
    public required TEvent Message { get; init; }

    /// <summary>
    ///     Gets the durable metadata applied when the message is enqueued.
    /// </summary>
    public OutboxEnqueueMetadata Metadata { get; init; } = OutboxEnqueueMetadata.Immediate;

    /// <summary>
    ///     Creates an enqueue item for one typed event with default metadata.
    /// </summary>
    /// <param name="message">The message instance to serialize and store.</param>
    /// <returns>An enqueue item that uses <see cref="OutboxEnqueueMetadata.Immediate" />.</returns>
    public static OutboxEnqueueItem<TEvent> From(TEvent message)
    {
        return new OutboxEnqueueItem<TEvent> { Message = message };
    }

    /// <summary>
    ///     Creates an enqueue item for one typed event with caller-supplied metadata.
    /// </summary>
    /// <param name="message">The message instance to serialize and store.</param>
    /// <param name="metadata">The durable metadata applied when the event is enqueued.</param>
    /// <returns>An enqueue item carrying the supplied metadata.</returns>
    public static OutboxEnqueueItem<TEvent> From(TEvent message, OutboxEnqueueMetadata metadata)
    {
        return new OutboxEnqueueItem<TEvent> { Message = message, Metadata = metadata };
    }

    /// <summary>
    ///     Creates an enqueue item that defers processor leasing until the specified UTC timestamp.
    /// </summary>
    /// <param name="message">The message instance to serialize and store.</param>
    /// <param name="visibleAfter">The earliest UTC timestamp at which the event may be leased.</param>
    /// <returns>An enqueue item with <see cref="MessageVisibility.At" /> visibility metadata.</returns>
    public static OutboxEnqueueItem<TEvent> ScheduledAt(TEvent message, DateTimeOffset visibleAfter)
    {
        return From(message) with
        {
            Metadata = OutboxEnqueueMetadata.Immediate with
            {
                Visibility = new MessageVisibility.At(visibleAfter)
            }
        };
    }

    /// <summary>
    ///     Creates an enqueue item that defers processor leasing until a relative delay elapses.
    /// </summary>
    /// <param name="message">The message instance to serialize and store.</param>
    /// <param name="delay">The non-negative delay before the event becomes visible.</param>
    /// <returns>An enqueue item with <see cref="MessageVisibility.After" /> visibility metadata.</returns>
    public static OutboxEnqueueItem<TEvent> ScheduledAfter(TEvent message, TimeSpan delay)
    {
        return From(message) with
        {
            Metadata = OutboxEnqueueMetadata.Immediate with
            {
                Visibility = new MessageVisibility.After(delay)
            }
        };
    }

    /// <summary>
    ///     Creates an enqueue item that stores an application-defined idempotency key with the envelope.
    /// </summary>
    /// <param name="message">The message instance to serialize and store.</param>
    /// <param name="idempotencyKey">The idempotency key used for insert-time deduplication.</param>
    /// <returns>An enqueue item with <see cref="Idempotency.Keyed" /> metadata.</returns>
    public static OutboxEnqueueItem<TEvent> WithIdempotency(TEvent message, string idempotencyKey)
    {
        return From(message) with
        {
            Metadata = OutboxEnqueueMetadata.Immediate with
            {
                Idempotency = new Idempotency.Keyed(idempotencyKey)
            }
        };
    }

    /// <summary>
    ///     Creates an enqueue item that stores a caller-supplied outbox message identifier.
    /// </summary>
    /// <param name="message">The message instance to serialize and store.</param>
    /// <param name="messageId">The message identifier supplied by the caller.</param>
    /// <returns>An enqueue item with <see cref="MessageIdentity.Supplied" /> metadata.</returns>
    public static OutboxEnqueueItem<TEvent> WithIdentity(TEvent message, Guid messageId)
    {
        return From(message) with
        {
            Metadata = OutboxEnqueueMetadata.Immediate with
            {
                Identity = new MessageIdentity.Supplied(messageId)
            }
        };
    }

    /// <summary>
    ///     Creates an enqueue item that stores an explicit publication topic or channel.
    /// </summary>
    /// <param name="message">The message instance to serialize and store.</param>
    /// <param name="topic">The topic or channel name dispatchers map to a transport target.</param>
    /// <returns>An enqueue item with <see cref="PublicationTarget.Topic" /> metadata.</returns>
    public static OutboxEnqueueItem<TEvent> WithTopic(TEvent message, string topic)
    {
        return From(message) with
        {
            Metadata = OutboxEnqueueMetadata.Immediate with
            {
                Target = new PublicationTarget.Topic(topic)
            }
        };
    }
}
