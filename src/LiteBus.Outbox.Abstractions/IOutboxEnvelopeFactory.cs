using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Outbox.Abstractions;

/// <summary>
///     Creates outbox envelopes from enqueue items using contract lookup, serialization, and payload protection.
/// </summary>
/// <remarks>
///     Envelope factories centralize metadata mapping so <see cref="IOutbox" />,
///     <see cref="ITransactionalOutbox" />, and Entity Framework Core staging share one creation path.
/// </remarks>
public interface IOutboxEnvelopeFactory
{
    /// <summary>
    ///     Creates one outbox envelope from a typed enqueue item.
    /// </summary>
    /// <typeparam name="TEvent">The compile-time event type. The runtime type of the item event is used for contract lookup.</typeparam>
    /// <param name="item">The enqueue command carrying the event instance and durable metadata.</param>
    /// <param name="cancellationToken">A token used to cancel serialization.</param>
    /// <returns>The outbox envelope ready for store persistence or staging.</returns>
    Task<OutboxEnvelope> CreateAsync<TEvent>(
        OutboxEnqueueItem<TEvent> item,
        CancellationToken cancellationToken = default)
        where TEvent : notnull;

    /// <summary>
    ///     Creates one outbox envelope from an enqueue item with an explicit runtime type.
    /// </summary>
    /// <param name="item">The enqueue command carrying the event instance, runtime type, and durable metadata.</param>
    /// <param name="cancellationToken">A token used to cancel serialization.</param>
    /// <returns>The outbox envelope ready for store persistence or staging.</returns>
    Task<OutboxEnvelope> CreateAsync(
        OutboxEnqueueItem item,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Creates multiple outbox envelopes from typed enqueue items.
    /// </summary>
    /// <typeparam name="TEvent">The shared compile-time event type. Each instance's runtime type is used for contract lookup.</typeparam>
    /// <param name="items">The enqueue commands to serialize.</param>
    /// <param name="cancellationToken">A token used to cancel serialization.</param>
    /// <returns>
    ///     Outbox envelopes in the same order as <paramref name="items" />.
    /// </returns>
    Task<IReadOnlyList<OutboxEnvelope>> CreateBatchAsync<TEvent>(
        IReadOnlyList<OutboxEnqueueItem<TEvent>> items,
        CancellationToken cancellationToken = default)
        where TEvent : notnull;

    /// <summary>
    ///     Creates multiple outbox envelopes from heterogeneous enqueue items.
    /// </summary>
    /// <param name="items">The enqueue commands carrying explicit runtime types and durable metadata.</param>
    /// <param name="cancellationToken">A token used to cancel serialization.</param>
    /// <returns>
    ///     Outbox envelopes in the same order as <paramref name="items" />.
    /// </returns>
    Task<IReadOnlyList<OutboxEnvelope>> CreateBatchAsync(
        IReadOnlyList<OutboxEnqueueItem> items,
        CancellationToken cancellationToken = default);
}