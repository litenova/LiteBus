using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Outbox.Abstractions;

/// <summary>
///     Shared outbox enqueue pipeline used by writer and transactional writer implementations.
/// </summary>
public sealed class OutboxWriterCore
{
    /// <summary>
    ///     Gets the factory used to create envelopes before persistence.
    /// </summary>
    private readonly IOutboxEnvelopeFactory _envelopeFactory;

    /// <summary>
    ///     Initializes a new instance of the <see cref="OutboxWriterCore" /> class.
    /// </summary>
    /// <param name="envelopeFactory">The factory used to create envelopes before persistence.</param>
    public OutboxWriterCore(IOutboxEnvelopeFactory envelopeFactory)
    {
        ArgumentNullException.ThrowIfNull(envelopeFactory);

        _envelopeFactory = envelopeFactory;
    }

    /// <summary>
    ///     Enqueues one typed event through the supplied persistence delegate.
    /// </summary>
    /// <typeparam name="TEvent">The compile-time event type associated with the enqueue command.</typeparam>
    /// <param name="item">The enqueue item describing the event and metadata.</param>
    /// <param name="persistAsync">The delegate that persists or stages the created envelope.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>The typed enqueue receipt returned to callers.</returns>
    public async Task<OutboxReceipt<TEvent>> EnqueueAsync<TEvent>(
        OutboxEnqueueItem<TEvent> item,
        Func<OutboxEnvelope, CancellationToken, Task<OutboxAppendResult>> persistAsync,
        CancellationToken cancellationToken)
        where TEvent : notnull
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(persistAsync);

        var envelope = await _envelopeFactory.CreateAsync(item, cancellationToken).ConfigureAwait(false);
        var appendResult = await persistAsync(envelope, cancellationToken).ConfigureAwait(false);

        return OutboxReceiptMapper.CreateTypedReceipt<TEvent>(
            appendResult.Envelope,
            item.Message.GetType(),
            appendResult.Outcome);
    }

    /// <summary>
    ///     Enqueues one untyped event through the supplied persistence delegate.
    /// </summary>
    /// <param name="item">The enqueue item describing the event, message type, and metadata.</param>
    /// <param name="persistAsync">The delegate that persists or stages the created envelope.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>The enqueue receipt returned to callers.</returns>
    public async Task<OutboxReceipt> EnqueueAsync(
        OutboxEnqueueItem item,
        Func<OutboxEnvelope, CancellationToken, Task<OutboxAppendResult>> persistAsync,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(persistAsync);

        var envelope = await _envelopeFactory.CreateAsync(item, cancellationToken).ConfigureAwait(false);
        var appendResult = await persistAsync(envelope, cancellationToken).ConfigureAwait(false);

        return OutboxReceiptMapper.CreateReceipt(
            appendResult.Envelope,
            item.MessageType,
            appendResult.Outcome);
    }

    /// <summary>
    ///     Enqueues a batch of untyped events through the supplied persistence delegate.
    /// </summary>
    /// <param name="items">The enqueue items describing each event, message type, and metadata pair.</param>
    /// <param name="persistBatchAsync">The delegate that persists or stages the created envelopes.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>The enqueue receipts returned to batch callers.</returns>
    public async Task<IReadOnlyList<OutboxReceipt>> EnqueueBatchAsync(
        IReadOnlyList<OutboxEnqueueItem> items,
        Func<IReadOnlyList<OutboxEnvelope>, CancellationToken, Task<IReadOnlyList<OutboxAppendResult>>> persistBatchAsync,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(persistBatchAsync);

        var envelopes = await _envelopeFactory.CreateBatchAsync(items, cancellationToken).ConfigureAwait(false);
        var appendResults = await persistBatchAsync(envelopes, cancellationToken).ConfigureAwait(false);

        if (appendResults.Count != items.Count)
        {
            throw new InvalidOperationException(
                $"The outbox append store returned {appendResults.Count} results for {items.Count} input items.");
        }

        var receipts = new OutboxReceipt[appendResults.Count];

        for (var index = 0; index < appendResults.Count; index++)
        {
            var appendResult = appendResults[index];
            receipts[index] = OutboxReceiptMapper.CreateReceipt(
                appendResult.Envelope,
                items[index].MessageType,
                appendResult.Outcome);
        }

        return receipts;
    }
}
