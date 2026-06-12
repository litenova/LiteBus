using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Inbox.Abstractions;

namespace LiteBus.Inbox;

/// <summary>
///     Accepts messages through a bound <see cref="ITransactionalInboxStore" /> without committing the caller transaction.
/// </summary>
public sealed class StoreBoundTransactionalInbox : ITransactionalInbox
{
    /// <summary>
    ///     Gets the shared acceptance pipeline used to create envelopes and map receipts.
    /// </summary>
    private readonly InboxAcceptanceService _acceptanceService;

    /// <summary>
    ///     Gets the bound inbox store participating in the caller transaction.
    /// </summary>
    private readonly ITransactionalInboxStore _store;

    /// <summary>
    ///     Initializes a new instance of the <see cref="StoreBoundTransactionalInbox" /> class.
    /// </summary>
    /// <param name="store">The bound inbox store participating in the caller transaction.</param>
    /// <param name="envelopeFactory">The factory used to create envelopes before store writes.</param>
    public StoreBoundTransactionalInbox(
        ITransactionalInboxStore store,
        IInboxEnvelopeFactory envelopeFactory)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(envelopeFactory);
        _store = store;
        _acceptanceService = new InboxAcceptanceService(envelopeFactory);
    }

    /// <inheritdoc />
    public Task<InboxReceipt<TMessage>> AcceptAsync<TMessage>(
        InboxAcceptItem<TMessage> item,
        CancellationToken cancellationToken = default)
        where TMessage : notnull
    {
        return _acceptanceService.AcceptAsync(
            item,
            (envelope, token) => _store.AddAsync(envelope, token),
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<InboxReceipt>> AcceptBatchAsync(
        IReadOnlyList<InboxAcceptItem> items,
        CancellationToken cancellationToken = default)
    {
        return _acceptanceService.AcceptBatchAsync(
            items,
            (envelopes, token) => _store.AddBatchAsync(envelopes, token),
            cancellationToken);
    }
}
