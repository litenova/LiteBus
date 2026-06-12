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
///         Register each stored message type in <see cref="LiteBus.Messaging.Abstractions.IMessageContractRegistry" />
///         with a
///         stable name and version, or apply <see cref="LiteBus.Messaging.Abstractions.MessageContractAttribute" /> and
///         scan the
///         assembly during module configuration. Closed generic message types are supported when each closed shape is
///         registered.
///         Open generic contract definitions are rejected. Contract lookup always uses <c>event.GetType()</c> for each
///         instance.
///     </para>
///     <para>
///         Deferred publication is expressed through <see cref="OutboxEnqueueMetadata.Visibility" /> on
///         <see cref="OutboxEnqueueItem{TEvent}.Metadata" /> rather than separate scheduler interfaces.
///     </para>
/// </remarks>
public interface IOutbox
{
    /// <summary>
    ///     Enqueues an event for later publication by an outbox processor.
    /// </summary>
    /// <typeparam name="TEvent">The compile-time event type. <c>event.GetType()</c> is always used for contract lookup.</typeparam>
    /// <param name="item">The enqueue command carrying the event instance and durable metadata.</param>
    /// <param name="cancellationToken">A token used to cancel serialization or the store write.</param>
    /// <returns>A receipt containing the outbox message id, contract reference, storage time, and trace metadata.</returns>
    Task<OutboxReceipt<TEvent>> EnqueueAsync<TEvent>(
        OutboxEnqueueItem<TEvent> item,
        CancellationToken cancellationToken = default)
        where TEvent : notnull;

    /// <summary>
    ///     Enqueues an event for later publication using default durable metadata.
    /// </summary>
    /// <typeparam name="TEvent">The compile-time event type. <c>event.GetType()</c> is always used for contract lookup.</typeparam>
    /// <param name="message">The event instance to serialize and store.</param>
    /// <param name="cancellationToken">A token used to cancel serialization or the store write.</param>
    /// <returns>A receipt containing the outbox message id, contract reference, storage time, and trace metadata.</returns>
    Task<OutboxReceipt<TEvent>> EnqueueAsync<TEvent>(
        TEvent message,
        CancellationToken cancellationToken = default)
        where TEvent : notnull
        => EnqueueAsync(OutboxEnqueueItem<TEvent>.From(message), cancellationToken);

    /// <summary>
    ///     Enqueues an event for later publication using an explicit runtime type for contract lookup.
    /// </summary>
    /// <param name="item">The enqueue command carrying the event instance, runtime type, and durable metadata.</param>
    /// <param name="cancellationToken">A token used to cancel serialization or the store write.</param>
    /// <returns>A receipt containing the outbox message id, contract reference, storage time, and trace metadata.</returns>
    Task<OutboxReceipt> EnqueueAsync(
        OutboxEnqueueItem item,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Enqueues multiple events for later publication in one store round trip.
    /// </summary>
    /// <typeparam name="TEvent">The shared compile-time event type. Each instance's runtime type is used for contract lookup.</typeparam>
    /// <param name="items">The enqueue commands to serialize and store.</param>
    /// <param name="cancellationToken">A token used to cancel serialization or the store write.</param>
    /// <returns>
    ///     Receipts containing message ids, contract references, storage times, and trace metadata in the same order as
    ///     <paramref name="items" />.
    /// </returns>
    Task<IReadOnlyList<OutboxReceipt<TEvent>>> EnqueueBatchAsync<TEvent>(
        IReadOnlyList<OutboxEnqueueItem<TEvent>> items,
        CancellationToken cancellationToken = default)
        where TEvent : notnull;

    /// <summary>
    ///     Enqueues multiple events for later publication in one store round trip.
    /// </summary>
    /// <param name="items">The enqueue commands carrying heterogeneous runtime types and durable metadata.</param>
    /// <param name="cancellationToken">A token used to cancel serialization or the store write.</param>
    /// <returns>
    ///     Receipts containing message ids, contract references, storage times, and trace metadata in the same order as
    ///     <paramref name="items" />.
    /// </returns>
    Task<IReadOnlyList<OutboxReceipt>> EnqueueBatchAsync(
        IReadOnlyList<OutboxEnqueueItem> items,
        CancellationToken cancellationToken = default);
}