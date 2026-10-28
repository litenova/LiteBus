using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Outbox.Abstractions;

/// <summary>
///     Creates outbox envelopes from enqueue items using contract lookup, serialization, and payload protection.
/// </summary>
/// <remarks>
///     Envelope factories centralize metadata mapping so <see cref="IOutbox" />,
///     <see cref="ITransactionalOutbox" />, and Entity Framework Core staging share one creation path. A distributed
///     trace context must be valid JSON text; creation throws <see cref="System.ArgumentException" /> otherwise, so
///     every provider fails fast instead of persisting a row it cannot read back.
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
    ///     Creates the outbox envelopes for one typed enqueue item, fanning out to one envelope per subscribed main handler when per-handler delivery is enabled.
    /// </summary>
    /// <typeparam name="TEvent">The compile-time event type. The runtime type of the item event is used for contract lookup.</typeparam>
    /// <param name="item">The enqueue command carrying the event instance and durable metadata.</param>
    /// <param name="cancellationToken">A token used to cancel serialization.</param>
    /// <returns>
    ///     One envelope per subscribed main handler when per-handler delivery is enabled; otherwise a single envelope.
    ///     Envelopes are ordered by handler full name.
    /// </returns>
    /// <remarks>
    ///     The default implementation returns the single envelope from <see cref="CreateAsync{TEvent}" />. Custom
    ///     factories inherit broadcast behavior unless they override this method.
    /// </remarks>
    async Task<IReadOnlyList<OutboxEnvelope>> CreateManyAsync<TEvent>(
        OutboxEnqueueItem<TEvent> item,
        CancellationToken cancellationToken = default)
        where TEvent : notnull
    {
        return [await CreateAsync(item, cancellationToken).ConfigureAwait(false)];
    }

    /// <summary>
    ///     Creates the outbox envelopes for one enqueue item with an explicit runtime type, fanning out to one envelope per subscribed main handler when per-handler delivery is enabled.
    /// </summary>
    /// <param name="item">The enqueue command carrying the event instance, runtime type, and durable metadata.</param>
    /// <param name="cancellationToken">A token used to cancel serialization.</param>
    /// <returns>
    ///     One envelope per subscribed main handler when per-handler delivery is enabled; otherwise a single envelope.
    ///     Envelopes are ordered by handler full name.
    /// </returns>
    /// <remarks>
    ///     The default implementation returns the single envelope from <see cref="CreateAsync" />. Custom factories
    ///     inherit broadcast behavior unless they override this method.
    /// </remarks>
    async Task<IReadOnlyList<OutboxEnvelope>> CreateManyAsync(
        OutboxEnqueueItem item,
        CancellationToken cancellationToken = default)
    {
        return [await CreateAsync(item, cancellationToken).ConfigureAwait(false)];
    }

    /// <summary>
    ///     Creates multiple outbox envelopes from typed enqueue items.
    /// </summary>
    /// <typeparam name="TEvent">The shared compile-time event type. Each instance's runtime type is used for contract lookup.</typeparam>
    /// <param name="items">The enqueue commands to serialize.</param>
    /// <param name="cancellationToken">A token used to cancel serialization.</param>
    /// <returns>
    ///     One envelope per item, or one envelope per subscribed main handler per item when per-handler delivery is
    ///     enabled, flattened in item order, then handler-name order within each item.
    /// </returns>
    /// <remarks>
    ///     Writer implementations fan out through <see cref="CreateManyAsync{TEvent}" />; overrides of this batch
    ///     method apply only to direct factory calls.
    /// </remarks>
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
    ///     One envelope per item, or one envelope per subscribed main handler per item when per-handler delivery is
    ///     enabled, flattened in item order, then handler-name order within each item.
    /// </returns>
    /// <remarks>
    ///     Writer implementations fan out through <see cref="CreateManyAsync" />; overrides of this batch method
    ///     apply only to direct factory calls.
    /// </remarks>
    Task<IReadOnlyList<OutboxEnvelope>> CreateBatchAsync(
        IReadOnlyList<OutboxEnqueueItem> items,
        CancellationToken cancellationToken = default);
}
