using LiteBus.Messaging.Abstractions;
using LiteBus.Outbox.Abstractions;

namespace LiteBus.Outbox.UnitTests;

/// <summary>
///     Builds outbox writers and enqueue items for unit tests using the production envelope factory path.
/// </summary>
internal static class OutboxWriterTestFactory
{
    /// <summary>
    ///     Creates an <see cref="Outbox" /> wired with an envelope factory for tests.
    /// </summary>
    /// <param name="store">The outbox store.</param>
    /// <param name="contractRegistry">The contract registry.</param>
    /// <param name="serializer">The message serializer.</param>
    /// <param name="clock">The time provider.</param>
    /// <param name="payloadProtector">The optional payload protector.</param>
    /// <returns>The configured outbox writer.</returns>
    internal static Outbox Create(
        IOutboxStore store,
        IContractReader contractRegistry,
        IMessageSerializer serializer,
        TimeProvider clock,
        IOutboxPayloadProtector? payloadProtector = null)
    {
        return new Outbox(
            store,
            new OutboxEnvelopeFactory(contractRegistry, serializer, clock, payloadProtector),
            clock);
    }

    /// <summary>
    ///     Creates an enqueue item with default immediate metadata.
    /// </summary>
    /// <typeparam name="TEvent">The compile-time event type.</typeparam>
    /// <param name="event">The event instance to enqueue.</param>
    /// <returns>An enqueue item for <see cref="IOutbox.EnqueueAsync{TEvent}" />.</returns>
    internal static OutboxEnqueueItem<TEvent> Item<TEvent>(TEvent @event)
        where TEvent : notnull
    {
        return OutboxEnqueueItems.From(@event);
    }

    /// <summary>
    ///     Creates an enqueue item with a caller-supplied message identifier.
    /// </summary>
    /// <typeparam name="TEvent">The compile-time event type.</typeparam>
    /// <param name="event">The event instance to enqueue.</param>
    /// <param name="messageId">The outbox message identifier to persist.</param>
    /// <returns>An enqueue item for <see cref="IOutbox.EnqueueAsync{TEvent}" />.</returns>
    internal static OutboxEnqueueItem<TEvent> ItemWithId<TEvent>(TEvent @event, Guid messageId)
        where TEvent : notnull
    {
        return OutboxEnqueueItems.WithIdentity(@event, messageId);
    }

    /// <summary>
    ///     Creates an enqueue item with caller-supplied durable metadata.
    /// </summary>
    /// <typeparam name="TEvent">The compile-time event type.</typeparam>
    /// <param name="event">The event instance to enqueue.</param>
    /// <param name="metadata">The durable metadata applied when the event is enqueued.</param>
    /// <returns>An enqueue item for <see cref="IOutbox.EnqueueAsync{TEvent}" />.</returns>
    internal static OutboxEnqueueItem<TEvent> ItemWithMetadata<TEvent>(TEvent @event, OutboxEnqueueMetadata metadata)
        where TEvent : notnull
    {
        return OutboxEnqueueItems.WithMetadata(@event, metadata);
    }
}