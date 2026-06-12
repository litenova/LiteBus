using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Outbox.Abstractions;

namespace LiteBus.Outbox;

/// <summary>
///     Enqueues events through a bound <see cref="ITransactionalOutboxStore" /> without committing the caller transaction.
/// </summary>
public sealed class StoreBoundTransactionalOutbox : ITransactionalOutbox
{
    /// <summary>
    ///     Gets the bound outbox store participating in the caller transaction.
    /// </summary>
    private readonly ITransactionalOutboxStore _store;

    /// <summary>
    ///     Gets the shared enqueue pipeline used to create envelopes and map receipts.
    /// </summary>
    private readonly OutboxWriterCore _writerCore;

    /// <summary>
    ///     Initializes a new instance of the <see cref="StoreBoundTransactionalOutbox" /> class.
    /// </summary>
    /// <param name="store">The bound outbox store participating in the caller transaction.</param>
    /// <param name="envelopeFactory">The factory used to create envelopes before store writes.</param>
    public StoreBoundTransactionalOutbox(
        ITransactionalOutboxStore store,
        IOutboxEnvelopeFactory envelopeFactory)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(envelopeFactory);

        _store = store;
        _writerCore = new OutboxWriterCore(envelopeFactory);
    }

    /// <inheritdoc />
    public Task<OutboxReceipt<TEvent>> EnqueueAsync<TEvent>(
        OutboxEnqueueItem<TEvent> item,
        CancellationToken cancellationToken = default)
        where TEvent : notnull
    {
        return _writerCore.EnqueueAsync(item, (envelope, token) => _store.AddAsync(envelope, token), cancellationToken);
    }

    /// <inheritdoc />
    public Task<OutboxReceipt> EnqueueAsync(
        OutboxEnqueueItem item,
        CancellationToken cancellationToken = default)
    {
        return _writerCore.EnqueueAsync(item, (envelope, token) => _store.AddAsync(envelope, token), cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<OutboxReceipt<TEvent>>> EnqueueBatchAsync<TEvent>(
        IReadOnlyList<OutboxEnqueueItem<TEvent>> items,
        CancellationToken cancellationToken = default)
        where TEvent : notnull
    {
        return _writerCore.EnqueueBatchAsync(
            items,
            (envelopes, token) => _store.AddBatchAsync(envelopes, token),
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<OutboxReceipt>> EnqueueBatchAsync(
        IReadOnlyList<OutboxEnqueueItem> items,
        CancellationToken cancellationToken = default)
    {
        return _writerCore.EnqueueBatchAsync(
            items,
            (envelopes, token) => _store.AddBatchAsync(envelopes, token),
            cancellationToken);
    }
}
