using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Outbox.Abstractions;

/// <summary>
///     Enqueues events for persistence in the caller's active transaction boundary.
/// </summary>
/// <remarks>
///     Use this API when domain work and outbox enqueue must commit or roll back together outside Entity Framework Core.
///     Bind a store through <see cref="ITransactionalOutboxStore" /> or register PostgreSQL ambient participation
///     with
///     <c>EnableAmbientTransactionProvider()</c>. The caller owns transaction commit. Processors continue to use
///     <see cref="IOutbox" /> with the singleton auto-commit store.
///     <para>
///         Deferred publication is expressed through <see cref="OutboxEnqueueMetadata.Visibility" /> on each
///         <see cref="OutboxEnqueueItem" /> rather than separate scheduler methods.
///     </para>
/// </remarks>
public interface ITransactionalOutbox
{
    /// <summary>
    ///     Enqueues an event for persistence in the caller's active transaction.
    /// </summary>
    /// <typeparam name="TEvent">The compile-time event type. The runtime type of the item event is used for contract lookup.</typeparam>
    /// <param name="item">The enqueue command carrying the event instance and durable metadata.</param>
    /// <param name="cancellationToken">A token used to cancel serialization.</param>
    /// <returns>A receipt describing the enqueued outbox message.</returns>
    Task<OutboxReceipt<TEvent>> EnqueueAsync<TEvent>(
        OutboxEnqueueItem<TEvent> item,
        CancellationToken cancellationToken = default)
        where TEvent : notnull;

    /// <summary>
    ///     Enqueues an event for persistence in the caller's active transaction using default durable metadata.
    /// </summary>
    /// <typeparam name="TEvent">The compile-time event type. The runtime type of the event is used for contract lookup.</typeparam>
    /// <param name="message">The event instance to serialize and store.</param>
    /// <param name="cancellationToken">A token used to cancel serialization.</param>
    /// <returns>A receipt describing the enqueued outbox message.</returns>
    Task<OutboxReceipt<TEvent>> EnqueueAsync<TEvent>(
        TEvent message,
        CancellationToken cancellationToken = default)
        where TEvent : notnull
        => EnqueueAsync(OutboxEnqueueItem<TEvent>.From(message), cancellationToken);

    /// <summary>
    ///     Enqueues an event for persistence in the caller's active transaction using an explicit runtime type.
    /// </summary>
    /// <param name="item">The enqueue command carrying the event instance, runtime type, and durable metadata.</param>
    /// <param name="cancellationToken">A token used to cancel serialization.</param>
    /// <returns>A receipt describing the enqueued outbox message.</returns>
    Task<OutboxReceipt> EnqueueAsync(
        OutboxEnqueueItem item,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Enqueues multiple events for persistence in one store round trip within the caller's transaction.
    /// </summary>
    /// <typeparam name="TEvent">The shared compile-time event type. Each instance's runtime type is used for contract lookup.</typeparam>
    /// <param name="items">The enqueue commands to serialize and persist.</param>
    /// <param name="cancellationToken">A token used to cancel serialization.</param>
    /// <returns>
    ///     Receipts describing enqueued outbox messages in the same order as <paramref name="items" />.
    /// </returns>
    Task<IReadOnlyList<OutboxReceipt<TEvent>>> EnqueueBatchAsync<TEvent>(
        IReadOnlyList<OutboxEnqueueItem<TEvent>> items,
        CancellationToken cancellationToken = default)
        where TEvent : notnull;

    /// <summary>
    ///     Enqueues multiple events for persistence in one store round trip within the caller's transaction.
    /// </summary>
    /// <param name="items">The enqueue commands carrying heterogeneous runtime types and durable metadata.</param>
    /// <param name="cancellationToken">A token used to cancel serialization.</param>
    /// <returns>
    ///     Receipts describing enqueued outbox messages in the same order as <paramref name="items" />.
    /// </returns>
    Task<IReadOnlyList<OutboxReceipt>> EnqueueBatchAsync(
        IReadOnlyList<OutboxEnqueueItem> items,
        CancellationToken cancellationToken = default);
}
