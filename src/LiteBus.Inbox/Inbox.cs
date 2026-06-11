using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Inbox.Abstractions;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions.DurableMessaging;

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
    ///     Gets the factory used to create envelopes before store writes.
    /// </summary>
    private readonly IInboxEnvelopeFactory _envelopeFactory;

    /// <summary>
    ///     Gets the inbox store used to persist newly accepted envelopes.
    /// </summary>
    private readonly IInboxStore _store;

    /// <summary>
    ///     Initializes a new instance of the <see cref="Inbox" /> class.
    /// </summary>
    /// <param name="store">The inbox store used to persist newly accepted envelopes.</param>
    /// <param name="envelopeFactory">The factory used to create envelopes before store writes.</param>
    /// <param name="clock">The time provider used to stamp acceptance time.</param>
    public Inbox(
        IInboxStore store,
        IInboxEnvelopeFactory envelopeFactory,
        TimeProvider clock)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _envelopeFactory = envelopeFactory ?? throw new ArgumentNullException(nameof(envelopeFactory));
        _ = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    /// <inheritdoc />
    public async Task<InboxReceipt> AcceptAsync<TMessage>(
        InboxAcceptItem<TMessage> item,
        CancellationToken cancellationToken = default)
        where TMessage : notnull
    {
        ArgumentNullException.ThrowIfNull(item);

        var envelope = await _envelopeFactory
            .CreateAsync(InboxAcceptItems.From(item), cancellationToken)
            .ConfigureAwait(false);

        var storedEnvelope = await _store.AddAsync(envelope, cancellationToken).ConfigureAwait(false);

        return CreateReceipt(storedEnvelope, item.Message.GetType());
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<InboxReceipt>> AcceptBatchAsync(
        IReadOnlyList<InboxAcceptItem> items,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(items);

        var envelopes = await _envelopeFactory.CreateBatchAsync(items, cancellationToken).ConfigureAwait(false);
        var stored = await _store.AddBatchAsync(envelopes, cancellationToken).ConfigureAwait(false);
        var receipts = new InboxReceipt[stored.Count];

        for (var index = 0; index < stored.Count; index++)
        {
            receipts[index] = CreateReceipt(stored[index], items[index].Message.GetType());
        }

        return receipts;
    }

    /// <summary>
    ///     Maps a stored envelope to an acceptance receipt.
    /// </summary>
    /// <param name="storedEnvelope">The envelope returned by the store.</param>
    /// <param name="messageType">The runtime message type used for contract lookup.</param>
    /// <returns>The acceptance receipt returned to callers.</returns>
    private static InboxReceipt CreateReceipt(InboxEnvelope storedEnvelope, Type messageType)
    {
        return new InboxReceipt
        {
            Id = storedEnvelope.Id,
            MessageType = messageType,
            Contract = new MessageContractReference
            {
                Name = storedEnvelope.ContractName,
                Version = storedEnvelope.ContractVersion
            },
            AcceptedAt = storedEnvelope.CreatedAt,
            Trace = DurableEnvelopeMetadataMapper.ResolveTrace(
                storedEnvelope.CorrelationId,
                storedEnvelope.CausationId,
                storedEnvelope.TraceContext),
            Tenant = DurableEnvelopeMetadataMapper.ResolveTenant(storedEnvelope.TenantId)
        };
    }
}