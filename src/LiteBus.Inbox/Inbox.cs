using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Inbox.Abstractions;

namespace LiteBus.Inbox;

/// <summary>
///     Default implementation of <see cref="IInbox" /> that serializes a message and writes it to the inbox store.
/// </summary>
/// <remarks>
///     <para>
///         Performs acceptance work only: contract lookup, serialization, metadata mapping, and write to the configured
///         <see cref="IInboxStore" />. Execution belongs to <see cref="PipelinedInboxProcessor" /> and the configured
///         <see cref="IInboxDispatcher" /> registered separately from the core inbox module.
///     </para>
///     <para>
///         The runtime message type is used for contract lookup so closed generic instances are stored with the contract
///         registered for that closed type.
///     </para>
/// </remarks>
public sealed class Inbox : IInbox
{
    /// <summary>
    ///     Gets the shared acceptance pipeline used to create envelopes and map receipts.
    /// </summary>
    private readonly InboxAcceptanceService _acceptanceService;

    /// <summary>
    ///     Gets the inbox store used to persist newly accepted envelopes.
    /// </summary>
    private readonly IInboxStore _store;

    /// <summary>
    ///     Initializes a new instance of the <see cref="Inbox" /> class.
    /// </summary>
    /// <param name="store">The inbox store used to persist newly accepted envelopes.</param>
    /// <param name="envelopeFactory">The factory used to create envelopes before store writes.</param>
    public Inbox(
        IInboxStore store,
        IInboxEnvelopeFactory envelopeFactory)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _acceptanceService = new InboxAcceptanceService(
            envelopeFactory ?? throw new ArgumentNullException(nameof(envelopeFactory)));
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
    public Task<InboxReceipt> AcceptAsync(
        InboxAcceptItem item,
        CancellationToken cancellationToken = default)
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
