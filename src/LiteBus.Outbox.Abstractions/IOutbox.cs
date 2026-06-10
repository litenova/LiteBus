using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Outbox.Abstractions;

/// <summary>
///     Accepts events into storage for later publication.
/// </summary>
/// <remarks>
///     <para>
///         Use this API when a message must survive process failure and be published after the surrounding state change
///         commits. The writer records an outbox envelope and returns an acceptance receipt; publication belongs to
///         <see cref="IOutboxProcessor" /> and a registered <see cref="IOutboxDispatcher" />.
///     </para>
///     <para>
///         Register each stored message type in <see cref="LiteBus.Messaging.Abstractions.IMessageContractRegistry" /> with a
///         stable name and version, or apply <see cref="LiteBus.Messaging.Abstractions.MessageContractAttribute" /> and scan the
///         assembly during module configuration. Closed generic message types are supported when each closed shape is registered.
///         Open generic contract definitions are rejected. Contract lookup always uses <c>event.GetType()</c> for each instance.
///     </para>
/// </remarks>
public interface IOutbox
{
    /// <summary>
    ///     Enqueues an event for later publication by an outbox processor.
    /// </summary>
    /// <typeparam name="TEvent">The compile-time event type. <c>event.GetType()</c> is always used for contract lookup.</typeparam>
    /// <param name="event">The event instance to serialize and store.</param>
    /// <param name="options">
    ///     Optional message metadata such as a caller-supplied message id, idempotency key, topic, correlation id,
    ///     causation id, and tenant id. Use <see cref="OutboxOptions.Id" /> when the caller already owns a stable event identifier.
    /// </param>
    /// <param name="cancellationToken">A token used to cancel serialization or the store write.</param>
    /// <returns>A receipt containing the outbox message id, contract name, version, storage time, and trace metadata.</returns>
    Task<OutboxReceipt<TEvent>> EnqueueAsync<TEvent>(
        TEvent @event,
        OutboxOptions? options = null,
        CancellationToken cancellationToken = default)
        where TEvent : notnull;

    /// <summary>
    ///     Enqueues multiple events for later publication in one store round trip.
    /// </summary>
    /// <param name="events">The event instances to serialize and store.</param>
    /// <param name="eventTypes">
    ///     The runtime event types used for contract lookup. Must contain the same number of entries as
    ///     <paramref name="events" />.
    /// </param>
    /// <param name="options">
    ///     Optional per-event metadata aligned with <paramref name="events" />. When omitted, default metadata is used for
    ///     every event. When supplied, the list length must match <paramref name="events" />.
    /// </param>
    /// <param name="cancellationToken">A token used to cancel serialization or the store write.</param>
    /// <returns>
    ///     Receipts containing message ids, contract names, versions, storage times, and trace metadata in the same order as
    ///     <paramref name="events" />.
    /// </returns>
    Task<IReadOnlyList<OutboxReceipt>> EnqueueBatchAsync(
        IReadOnlyList<object> events,
        IReadOnlyList<Type> eventTypes,
        IReadOnlyList<OutboxOptions?>? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Enqueues multiple events for later publication in one store round trip.
    /// </summary>
    /// <typeparam name="TEvent">The shared compile-time event type. Each instance's runtime type is used for contract lookup.</typeparam>
    /// <param name="events">The event instances to serialize and store.</param>
    /// <param name="options">
    ///     Optional per-event metadata aligned with <paramref name="events" />. When omitted, default metadata is used for
    ///     every event.
    /// </param>
    /// <param name="cancellationToken">A token used to cancel serialization or the store write.</param>
    /// <returns>
    ///     Receipts containing message ids, contract names, versions, storage times, and trace metadata in the same order as
    ///     <paramref name="events" />.
    /// </returns>
    Task<IReadOnlyList<OutboxReceipt<TEvent>>> EnqueueBatchAsync<TEvent>(
        IReadOnlyList<TEvent> events,
        IReadOnlyList<OutboxOptions?>? options = null,
        CancellationToken cancellationToken = default)
        where TEvent : notnull;
}
