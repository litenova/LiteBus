using System;
using LiteBus.Messaging.Abstractions.DurableMessaging;

namespace LiteBus.Outbox.Abstractions;

/// <summary>
///     Provides factory helpers for constructing <see cref="OutboxEnqueueItem" /> and
///     <see cref="OutboxEnqueueItem{TEvent}" /> values.
/// </summary>
public static class OutboxEnqueueItems
{
    /// <summary>
    ///     Creates an enqueue item for one typed event with default metadata.
    /// </summary>
    /// <typeparam name="TEvent">The compile-time event type associated with the enqueue command.</typeparam>
    /// <param name="event">The event instance to serialize and store.</param>
    /// <returns>An enqueue item that uses <see cref="OutboxEnqueueMetadata.Immediate" />.</returns>
    public static OutboxEnqueueItem<TEvent> From<TEvent>(TEvent @event)
        where TEvent : notnull
    {
        return new OutboxEnqueueItem<TEvent> { Event = @event };
    }

    /// <summary>
    ///     Creates an enqueue item for one event with an explicit runtime type and default metadata.
    /// </summary>
    /// <param name="event">The event instance to serialize and store.</param>
    /// <param name="eventType">The runtime event type used for contract lookup.</param>
    /// <returns>An enqueue item that uses <see cref="OutboxEnqueueMetadata.Immediate" />.</returns>
    public static OutboxEnqueueItem From(object @event, Type eventType)
    {
        return new OutboxEnqueueItem { Event = @event, EventType = eventType };
    }

    /// <summary>
    ///     Creates an enqueue item for one typed event with caller-supplied metadata.
    /// </summary>
    /// <typeparam name="TEvent">The compile-time event type associated with the enqueue command.</typeparam>
    /// <param name="event">The event instance to serialize and store.</param>
    /// <param name="metadata">The durable metadata applied when the event is enqueued.</param>
    /// <returns>An enqueue item carrying the supplied metadata.</returns>
    public static OutboxEnqueueItem<TEvent> WithMetadata<TEvent>(TEvent @event, OutboxEnqueueMetadata metadata)
        where TEvent : notnull
    {
        return new OutboxEnqueueItem<TEvent> { Event = @event, Metadata = metadata };
    }

    /// <summary>
    ///     Creates an enqueue item for one event with an explicit runtime type and caller-supplied metadata.
    /// </summary>
    /// <param name="event">The event instance to serialize and store.</param>
    /// <param name="eventType">The runtime event type used for contract lookup.</param>
    /// <param name="metadata">The durable metadata applied when the event is enqueued.</param>
    /// <returns>An enqueue item carrying the supplied metadata.</returns>
    public static OutboxEnqueueItem WithMetadata(object @event, Type eventType, OutboxEnqueueMetadata metadata)
    {
        return new OutboxEnqueueItem { Event = @event, EventType = eventType, Metadata = metadata };
    }

    /// <summary>
    ///     Creates an enqueue item that defers processor leasing until the specified UTC timestamp.
    /// </summary>
    /// <typeparam name="TEvent">The compile-time event type associated with the enqueue command.</typeparam>
    /// <param name="event">The event instance to serialize and store.</param>
    /// <param name="visibleAfter">The earliest UTC timestamp at which the event may be leased.</param>
    /// <returns>An enqueue item with <see cref="MessageVisibility.At" /> visibility metadata.</returns>
    public static OutboxEnqueueItem<TEvent> ScheduledAt<TEvent>(TEvent @event, DateTimeOffset visibleAfter)
        where TEvent : notnull
    {
        return From(@event) with
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
    /// <typeparam name="TEvent">The compile-time event type associated with the enqueue command.</typeparam>
    /// <param name="event">The event instance to serialize and store.</param>
    /// <param name="delay">The non-negative delay before the event becomes visible.</param>
    /// <returns>An enqueue item with <see cref="MessageVisibility.After" /> visibility metadata.</returns>
    public static OutboxEnqueueItem<TEvent> ScheduledAfter<TEvent>(TEvent @event, TimeSpan delay)
        where TEvent : notnull
    {
        return From(@event) with
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
    /// <typeparam name="TEvent">The compile-time event type associated with the enqueue command.</typeparam>
    /// <param name="event">The event instance to serialize and store.</param>
    /// <param name="idempotencyKey">The idempotency key used for insert-time deduplication.</param>
    /// <returns>An enqueue item with <see cref="Idempotency.Keyed" /> metadata.</returns>
    public static OutboxEnqueueItem<TEvent> WithIdempotency<TEvent>(TEvent @event, string idempotencyKey)
        where TEvent : notnull
    {
        return From(@event) with
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
    /// <typeparam name="TEvent">The compile-time event type associated with the enqueue command.</typeparam>
    /// <param name="event">The event instance to serialize and store.</param>
    /// <param name="messageId">The message identifier supplied by the caller.</param>
    /// <returns>An enqueue item with <see cref="MessageIdentity.Supplied" /> metadata.</returns>
    public static OutboxEnqueueItem<TEvent> WithIdentity<TEvent>(TEvent @event, Guid messageId)
        where TEvent : notnull
    {
        return From(@event) with
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
    /// <typeparam name="TEvent">The compile-time event type associated with the enqueue command.</typeparam>
    /// <param name="event">The event instance to serialize and store.</param>
    /// <param name="topic">The topic or channel name dispatchers map to a transport target.</param>
    /// <returns>An enqueue item with <see cref="PublicationTarget.Topic" /> metadata.</returns>
    public static OutboxEnqueueItem<TEvent> WithTopic<TEvent>(TEvent @event, string topic)
        where TEvent : notnull
    {
        return From(@event) with
        {
            Metadata = OutboxEnqueueMetadata.Immediate with
            {
                Target = new PublicationTarget.Topic(topic)
            }
        };
    }
}