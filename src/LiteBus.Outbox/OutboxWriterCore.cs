using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Outbox.Abstractions;

namespace LiteBus.Outbox;

/// <summary>
///     Shared outbox enqueue pipeline used by writer and transactional writer implementations.
/// </summary>
internal sealed class OutboxWriterCore
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
        _envelopeFactory = envelopeFactory ?? throw new ArgumentNullException(nameof(envelopeFactory));
    }

    /// <summary>
    ///     Enqueues one typed event through the supplied persistence delegate.
    /// </summary>
    /// <typeparam name="TEvent">The compile-time event type associated with the enqueue command.</typeparam>
    /// <param name="item">The enqueue item describing the event and metadata.</param>
    /// <param name="persistAsync">The delegate that persists or stages the created envelope.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>The typed enqueue receipt returned to callers.</returns>
    internal async Task<OutboxReceipt<TEvent>> EnqueueAsync<TEvent>(
        OutboxEnqueueItem<TEvent> item,
        Func<OutboxEnvelope, CancellationToken, Task<OutboxEnvelope>> persistAsync,
        CancellationToken cancellationToken)
        where TEvent : notnull
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(persistAsync);

        var envelope = await _envelopeFactory.CreateAsync(item, cancellationToken).ConfigureAwait(false);
        var storedEnvelope = await persistAsync(envelope, cancellationToken).ConfigureAwait(false);

        return OutboxReceiptMapper.CreateTypedReceipt<TEvent>(storedEnvelope, item.Message.GetType());
    }

    /// <summary>
    ///     Enqueues one untyped event through the supplied persistence delegate.
    /// </summary>
    /// <param name="item">The enqueue item describing the event, message type, and metadata.</param>
    /// <param name="persistAsync">The delegate that persists or stages the created envelope.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>The enqueue receipt returned to callers.</returns>
    internal async Task<OutboxReceipt> EnqueueAsync(
        OutboxEnqueueItem item,
        Func<OutboxEnvelope, CancellationToken, Task<OutboxEnvelope>> persistAsync,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(persistAsync);

        var envelope = await _envelopeFactory.CreateAsync(item, cancellationToken).ConfigureAwait(false);
        var storedEnvelope = await persistAsync(envelope, cancellationToken).ConfigureAwait(false);

        return OutboxReceiptMapper.CreateReceipt(storedEnvelope, item.MessageType);
    }

    /// <summary>
    ///     Enqueues a batch of typed events through the supplied persistence delegate.
    /// </summary>
    /// <typeparam name="TEvent">The compile-time event type associated with the batch.</typeparam>
    /// <param name="items">The enqueue items describing each event and metadata pair.</param>
    /// <param name="persistBatchAsync">The delegate that persists or stages the created envelopes.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>The typed enqueue receipts returned to batch callers.</returns>
    internal async Task<IReadOnlyList<OutboxReceipt<TEvent>>> EnqueueBatchAsync<TEvent>(
        IReadOnlyList<OutboxEnqueueItem<TEvent>> items,
        Func<IReadOnlyList<OutboxEnvelope>, CancellationToken, Task<IReadOnlyList<OutboxEnvelope>>> persistBatchAsync,
        CancellationToken cancellationToken)
        where TEvent : notnull
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(persistBatchAsync);

        var envelopes = await _envelopeFactory.CreateBatchAsync(items, cancellationToken).ConfigureAwait(false);
        var stored = await persistBatchAsync(envelopes, cancellationToken).ConfigureAwait(false);
        var receipts = new OutboxReceipt<TEvent>[stored.Count];

        for (var index = 0; index < stored.Count; index++)
        {
            receipts[index] = OutboxReceiptMapper.CreateTypedReceipt<TEvent>(stored[index], items[index].Message.GetType());
        }

        return receipts;
    }

    /// <summary>
    ///     Enqueues a batch of untyped events through the supplied persistence delegate.
    /// </summary>
    /// <param name="items">The enqueue items describing each event, message type, and metadata pair.</param>
    /// <param name="persistBatchAsync">The delegate that persists or stages the created envelopes.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>The enqueue receipts returned to batch callers.</returns>
    internal async Task<IReadOnlyList<OutboxReceipt>> EnqueueBatchAsync(
        IReadOnlyList<OutboxEnqueueItem> items,
        Func<IReadOnlyList<OutboxEnvelope>, CancellationToken, Task<IReadOnlyList<OutboxEnvelope>>> persistBatchAsync,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(persistBatchAsync);

        var envelopes = await _envelopeFactory.CreateBatchAsync(items, cancellationToken).ConfigureAwait(false);
        var stored = await persistBatchAsync(envelopes, cancellationToken).ConfigureAwait(false);
        var receipts = new OutboxReceipt[stored.Count];

        for (var index = 0; index < stored.Count; index++)
        {
            receipts[index] = OutboxReceiptMapper.CreateReceipt(stored[index], items[index].MessageType);
        }

        return receipts;
    }
}
