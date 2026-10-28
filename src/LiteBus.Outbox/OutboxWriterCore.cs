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
    /// <remarks>
    ///     Under per-handler delivery the factory returns one envelope per subscribed main handler. Every envelope is
    ///     persisted in handler-name order and the returned receipt describes the first one; the remaining envelopes
    ///     carry their own identifiers and receipts are available through the store query roles. This overload persists
    ///     envelopes one at a time; prefer the batch-delegate overload so the whole fan-out commits atomically.
    /// </remarks>
    public async Task<OutboxReceipt<TEvent>> EnqueueAsync<TEvent>(
        OutboxEnqueueItem<TEvent> item,
        Func<OutboxEnvelope, CancellationToken, Task<OutboxAppendResult>> persistAsync,
        CancellationToken cancellationToken)
        where TEvent : notnull
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(persistAsync);

        var envelopes = await _envelopeFactory.CreateManyAsync(item, cancellationToken).ConfigureAwait(false);

        if (envelopes.Count == 0)
        {
            throw new InvalidOperationException(
                "The outbox envelope factory returned no envelopes for the enqueue item.");
        }

        var firstResult = await persistAsync(envelopes[0], cancellationToken).ConfigureAwait(false);

        for (var index = 1; index < envelopes.Count; index++)
        {
            await persistAsync(envelopes[index], cancellationToken).ConfigureAwait(false);
        }

        return OutboxReceiptMapper.CreateTypedReceipt<TEvent>(
            firstResult.Envelope,
            item.Message.GetType(),
            firstResult.Outcome);
    }

    /// <summary>
    ///     Enqueues one typed event by persisting the whole per-handler fan-out in one batch call.
    /// </summary>
    /// <typeparam name="TEvent">The compile-time event type associated with the enqueue command.</typeparam>
    /// <param name="item">The enqueue item describing the event and metadata.</param>
    /// <param name="persistBatchAsync">The delegate that persists or stages the created envelopes atomically.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>The typed enqueue receipt returned to callers.</returns>
    /// <remarks>
    ///     Under per-handler delivery the factory returns one envelope per subscribed main handler. Every envelope is
    ///     persisted in handler-name order through one batch call and the returned receipt describes the first one; the
    ///     remaining envelopes carry their own identifiers and receipts are available through the store query roles.
    /// </remarks>
    public async Task<OutboxReceipt<TEvent>> EnqueueAsync<TEvent>(
        OutboxEnqueueItem<TEvent> item,
        Func<IReadOnlyList<OutboxEnvelope>, CancellationToken, Task<IReadOnlyList<OutboxAppendResult>>> persistBatchAsync,
        CancellationToken cancellationToken)
        where TEvent : notnull
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(persistBatchAsync);

        var envelopes = await _envelopeFactory.CreateManyAsync(item, cancellationToken).ConfigureAwait(false);

        if (envelopes.Count == 0)
        {
            throw new InvalidOperationException(
                "The outbox envelope factory returned no envelopes for the enqueue item.");
        }

        var results = await persistBatchAsync(envelopes, cancellationToken).ConfigureAwait(false);

        if (results.Count != envelopes.Count)
        {
            throw new InvalidOperationException(
                $"The outbox append store returned {results.Count} results for {envelopes.Count} envelopes.");
        }

        return OutboxReceiptMapper.CreateTypedReceipt<TEvent>(
            results[0].Envelope,
            item.Message.GetType(),
            results[0].Outcome);
    }

    /// <summary>
    ///     Enqueues one untyped event through the supplied persistence delegate.
    /// </summary>
    /// <param name="item">The enqueue item describing the event, message type, and metadata.</param>
    /// <param name="persistAsync">The delegate that persists or stages the created envelope.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>The enqueue receipt returned to callers.</returns>
    /// <remarks>
    ///     Under per-handler delivery the factory returns one envelope per subscribed main handler. Every envelope is
    ///     persisted in handler-name order and the returned receipt describes the first one; the remaining envelopes
    ///     carry their own identifiers and receipts are available through the store query roles. This overload persists
    ///     envelopes one at a time; prefer the batch-delegate overload so the whole fan-out commits atomically.
    /// </remarks>
    public async Task<OutboxReceipt> EnqueueAsync(
        OutboxEnqueueItem item,
        Func<OutboxEnvelope, CancellationToken, Task<OutboxAppendResult>> persistAsync,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(persistAsync);

        var envelopes = await _envelopeFactory.CreateManyAsync(item, cancellationToken).ConfigureAwait(false);

        if (envelopes.Count == 0)
        {
            throw new InvalidOperationException(
                "The outbox envelope factory returned no envelopes for the enqueue item.");
        }

        var firstResult = await persistAsync(envelopes[0], cancellationToken).ConfigureAwait(false);

        for (var index = 1; index < envelopes.Count; index++)
        {
            await persistAsync(envelopes[index], cancellationToken).ConfigureAwait(false);
        }

        return OutboxReceiptMapper.CreateReceipt(
            firstResult.Envelope,
            item.MessageType,
            firstResult.Outcome);
    }

    /// <summary>
    ///     Enqueues one untyped event by persisting the whole per-handler fan-out in one batch call.
    /// </summary>
    /// <param name="item">The enqueue item describing the event, message type, and metadata.</param>
    /// <param name="persistBatchAsync">The delegate that persists or stages the created envelopes atomically.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>The enqueue receipt returned to callers.</returns>
    /// <remarks>
    ///     Under per-handler delivery the factory returns one envelope per subscribed main handler. Every envelope is
    ///     persisted in handler-name order through one batch call and the returned receipt describes the first one; the
    ///     remaining envelopes carry their own identifiers and receipts are available through the store query roles.
    /// </remarks>
    public async Task<OutboxReceipt> EnqueueAsync(
        OutboxEnqueueItem item,
        Func<IReadOnlyList<OutboxEnvelope>, CancellationToken, Task<IReadOnlyList<OutboxAppendResult>>> persistBatchAsync,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(persistBatchAsync);

        var envelopes = await _envelopeFactory.CreateManyAsync(item, cancellationToken).ConfigureAwait(false);

        if (envelopes.Count == 0)
        {
            throw new InvalidOperationException(
                "The outbox envelope factory returned no envelopes for the enqueue item.");
        }

        var results = await persistBatchAsync(envelopes, cancellationToken).ConfigureAwait(false);

        if (results.Count != envelopes.Count)
        {
            throw new InvalidOperationException(
                $"The outbox append store returned {results.Count} results for {envelopes.Count} envelopes.");
        }

        return OutboxReceiptMapper.CreateReceipt(
            results[0].Envelope,
            item.MessageType,
            results[0].Outcome);
    }

    /// <summary>
    ///     Enqueues a batch of untyped events through the supplied persistence delegate.
    /// </summary>
    /// <param name="items">The enqueue items describing each event, message type, and metadata pair.</param>
    /// <param name="persistBatchAsync">The delegate that persists or stages the created envelopes.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>The enqueue receipts returned to batch callers.</returns>
    /// <remarks>
    ///     Under per-handler delivery each item fans out to one envelope per subscribed main handler and receipts are
    ///     flattened in item order, then handler-name order within each item.
    /// </remarks>
    public async Task<IReadOnlyList<OutboxReceipt>> EnqueueBatchAsync(
        IReadOnlyList<OutboxEnqueueItem> items,
        Func<IReadOnlyList<OutboxEnvelope>, CancellationToken, Task<IReadOnlyList<OutboxAppendResult>>> persistBatchAsync,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(persistBatchAsync);

        var tasks = new Task<IReadOnlyList<OutboxEnvelope>>[items.Count];

        for (var index = 0; index < items.Count; index++)
        {
            tasks[index] = _envelopeFactory.CreateManyAsync(items[index], cancellationToken);
        }

        var perItemEnvelopes = await Task.WhenAll(tasks).ConfigureAwait(false);
        List<OutboxEnvelope> envelopes = [];
        List<Type> messageTypes = [];

        for (var index = 0; index < perItemEnvelopes.Length; index++)
        {
            var expanded = perItemEnvelopes[index];

            for (var envelopeIndex = 0; envelopeIndex < expanded.Count; envelopeIndex++)
            {
                envelopes.Add(expanded[envelopeIndex]);
                messageTypes.Add(items[index].MessageType);
            }
        }

        var appendResults = await persistBatchAsync(envelopes, cancellationToken).ConfigureAwait(false);

        if (appendResults.Count != envelopes.Count)
        {
            throw new InvalidOperationException(
                $"The outbox append store returned {appendResults.Count} results for {envelopes.Count} envelopes.");
        }

        var receipts = new OutboxReceipt[appendResults.Count];

        for (var index = 0; index < appendResults.Count; index++)
        {
            var appendResult = appendResults[index];
            receipts[index] = OutboxReceiptMapper.CreateReceipt(
                appendResult.Envelope,
                messageTypes[index],
                appendResult.Outcome);
        }

        return receipts;
    }

    /// <summary>
    ///     Persists the envelopes of one enqueue: a single envelope through <see cref="IOutboxStore.AddAsync" />, and a
    ///     fan-out of several through one <see cref="IOutboxStore.AddBatchAsync" /> call so the store writes them atomically.
    /// </summary>
    /// <param name="store">The store that persists the envelopes.</param>
    /// <param name="envelopes">The envelopes created for one enqueue item or batch.</param>
    /// <param name="cancellationToken">A token that cancels the write.</param>
    /// <returns>One append result per envelope, in input order.</returns>
    internal static async Task<IReadOnlyList<OutboxAppendResult>> AppendAsync(
        IOutboxStore store,
        IReadOnlyList<OutboxEnvelope> envelopes,
        CancellationToken cancellationToken)
    {
        if (envelopes.Count == 1)
        {
            return [await store.AddAsync(envelopes[0], cancellationToken).ConfigureAwait(false)];
        }

        return await store.AddBatchAsync(envelopes, cancellationToken).ConfigureAwait(false);
    }
}
