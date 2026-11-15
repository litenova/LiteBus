using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Outbox.Abstractions;

/// <summary>
///     Enqueues events through a bound <see cref="ITransactionalOutboxStore" /> without committing the caller transaction.
/// </summary>
public sealed class StoreBoundTransactionalOutbox : ITransactionalOutbox
{
    /// <summary>
    ///     Gets the factory used to create envelopes before store writes.
    /// </summary>
    private readonly IOutboxEnvelopeFactory _envelopeFactory;

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
        _envelopeFactory = envelopeFactory;
        _writerCore = new OutboxWriterCore(envelopeFactory);
    }

    /// <inheritdoc />
    public Task<OutboxReceipt<TEvent>> EnqueueAsync<TEvent>(
        OutboxEnqueueItem<TEvent> item,
        CancellationToken cancellationToken = default)
        where TEvent : notnull
    {
        return _writerCore.EnqueueAsync(item, AppendAndRecordAsync, cancellationToken);
    }

    /// <inheritdoc />
    public Task<OutboxReceipt> EnqueueAsync(
        OutboxEnqueueItem item,
        CancellationToken cancellationToken = default)
    {
        return _writerCore.EnqueueAsync(item, AppendAndRecordAsync, cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<OutboxReceipt>> EnqueueBatchAsync(
        IReadOnlyList<OutboxEnqueueItem> items,
        CancellationToken cancellationToken = default)
    {
        return _writerCore.EnqueueBatchAsync(
            items,
            AppendAndRecordAsync,
            cancellationToken);
    }

    /// <summary>
    ///     Appends envelopes to the bound store and reports newly enqueued immediate envelopes for dispatch after the commit.
    /// </summary>
    /// <param name="envelopes">The envelopes created for the current enqueue attempt.</param>
    /// <param name="cancellationToken">A token that cancels the write.</param>
    /// <returns>One append result per envelope, in input order.</returns>
    private async Task<IReadOnlyList<OutboxAppendResult>> AppendAndRecordAsync(
        IReadOnlyList<OutboxEnvelope> envelopes,
        CancellationToken cancellationToken)
    {
        var results = await OutboxWriterCore.AppendAsync(_store, envelopes, cancellationToken).ConfigureAwait(false);

        RecordStagedImmediateEnvelopes(results);

        return results;
    }

    /// <summary>
    ///     Reports newly enqueued envelopes to the factory so it records the immediate ones for dispatch after the commit.
    /// </summary>
    /// <param name="results">The append results returned by the bound store.</param>
    private void RecordStagedImmediateEnvelopes(IReadOnlyList<OutboxAppendResult> results)
    {
        if (_envelopeFactory is not OutboxEnvelopeFactory factory)
        {
            return;
        }

        for (var index = 0; index < results.Count; index++)
        {
            var result = results[index];

            if (result.Outcome == OutboxEnqueueOutcome.Enqueued)
            {
                factory.RecordStagedImmediateEnvelope(result.Envelope.Id);
            }
        }
    }
}
