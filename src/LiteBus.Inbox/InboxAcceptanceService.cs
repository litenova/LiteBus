using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Inbox.Abstractions;

/// <summary>
///     Shared inbox acceptance pipeline used by writer and transactional writer implementations.
/// </summary>
public sealed class InboxAcceptanceService
{
    /// <summary>
    ///     Gets the factory used to create envelopes before persistence.
    /// </summary>
    private readonly IInboxEnvelopeFactory _envelopeFactory;

    /// <summary>
    ///     Initializes a new instance of the <see cref="InboxAcceptanceService" /> class.
    /// </summary>
    /// <param name="envelopeFactory">The factory used to create envelopes before persistence.</param>
    public InboxAcceptanceService(IInboxEnvelopeFactory envelopeFactory)
    {
        ArgumentNullException.ThrowIfNull(envelopeFactory);
        _envelopeFactory = envelopeFactory;
    }

    /// <summary>
    ///     Accepts one typed message through the supplied persistence delegate.
    /// </summary>
    /// <typeparam name="TMessage">The compile-time message type associated with the acceptance command.</typeparam>
    /// <param name="item">The acceptance item describing the message and metadata.</param>
    /// <param name="persistAsync">The delegate that persists or stages the created envelope.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>The typed acceptance receipt returned to callers.</returns>
    public async Task<InboxReceipt<TMessage>> AcceptAsync<TMessage>(
        InboxAcceptItem<TMessage> item,
        Func<InboxEnvelope, CancellationToken, Task<InboxAppendResult>> persistAsync,
        CancellationToken cancellationToken)
        where TMessage : notnull
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(persistAsync);

        var envelope = await _envelopeFactory
            .CreateAsync(InboxAcceptItem.From(item), cancellationToken)
            .ConfigureAwait(false);

        var appendResult = await persistAsync(envelope, cancellationToken).ConfigureAwait(false);

        return InboxReceiptMapper.CreateTypedReceipt<TMessage>(
            appendResult.Envelope,
            item.Message.GetType(),
            appendResult.Outcome);
    }

    /// <summary>
    ///     Accepts one untyped message through the supplied persistence delegate.
    /// </summary>
    /// <param name="item">The acceptance item describing the message and metadata.</param>
    /// <param name="persistAsync">The delegate that persists or stages the created envelope.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>The acceptance receipt returned to callers.</returns>
    public async Task<InboxReceipt> AcceptAsync(
        InboxAcceptItem item,
        Func<InboxEnvelope, CancellationToken, Task<InboxAppendResult>> persistAsync,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(persistAsync);

        var envelope = await _envelopeFactory.CreateAsync(item, cancellationToken).ConfigureAwait(false);
        var appendResult = await persistAsync(envelope, cancellationToken).ConfigureAwait(false);

        return InboxReceiptMapper.CreateUntypedReceipt(
            appendResult.Envelope,
            item.Message.GetType(),
            appendResult.Outcome);
    }

    /// <summary>
    ///     Accepts a batch of untyped messages through the supplied persistence delegate.
    /// </summary>
    /// <param name="items">The acceptance items describing each message and metadata pair.</param>
    /// <param name="persistBatchAsync">The delegate that persists or stages the created envelopes.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>The acceptance receipts returned to batch callers.</returns>
    public async Task<IReadOnlyList<InboxReceipt>> AcceptBatchAsync(
        IReadOnlyList<InboxAcceptItem> items,
        Func<IReadOnlyList<InboxEnvelope>, CancellationToken, Task<IReadOnlyList<InboxAppendResult>>> persistBatchAsync,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(persistBatchAsync);

        var envelopes = await _envelopeFactory.CreateBatchAsync(items, cancellationToken).ConfigureAwait(false);
        var appendResults = await persistBatchAsync(envelopes, cancellationToken).ConfigureAwait(false);

        if (appendResults.Count != items.Count)
        {
            throw new InvalidOperationException(
                $"The inbox append store returned {appendResults.Count} results for {items.Count} input items.");
        }

        var receipts = new InboxReceipt[appendResults.Count];

        for (var index = 0; index < appendResults.Count; index++)
        {
            var appendResult = appendResults[index];
            receipts[index] = InboxReceiptMapper.CreateUntypedReceipt(
                appendResult.Envelope,
                items[index].MessageType ?? items[index].Message.GetType(),
                appendResult.Outcome);
        }

        return receipts;
    }
}
