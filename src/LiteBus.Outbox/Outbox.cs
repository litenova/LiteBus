using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions.DurableMessaging;
using LiteBus.Outbox.Abstractions;

namespace LiteBus.Outbox;

/// <summary>
///     Default writer that turns an event instance into an outbox envelope.
/// </summary>
/// <remarks>
///     <para>
///         The writer performs only acceptance work: contract lookup, serialization, metadata mapping, and append to the
///         configured <see cref="IOutboxStore" />. It does not publish the event. Publication belongs to
///         <see cref="PipelinedOutboxProcessor" /> and the configured <see cref="IOutboxDispatcher" />.
///     </para>
///     <para>
///         Contract lookup always uses <c>event.GetType()</c> so closed generic event instances are stored with the
///         contract
///         registered for that closed type. Stable message identity is supplied through
///         <see cref="OutboxEnqueueMetadata.Identity" />.
///     </para>
/// </remarks>
public sealed class Outbox : IOutbox
{
    /// <summary>
    ///     Gets the factory used to create envelopes before store writes.
    /// </summary>
    private readonly IOutboxEnvelopeFactory _envelopeFactory;

    /// <summary>
    ///     Gets the outbox writer store used to persist newly accepted envelopes.
    /// </summary>
    private readonly IOutboxStore _store;

    /// <summary>
    ///     Initializes a new instance of the <see cref="Outbox" /> class.
    /// </summary>
    /// <param name="store">The outbox writer store used to persist newly accepted envelopes.</param>
    /// <param name="envelopeFactory">The factory used to create envelopes before store writes.</param>
    /// <param name="clock">The time provider used to stamp storage time.</param>
    public Outbox(
        IOutboxStore store,
        IOutboxEnvelopeFactory envelopeFactory,
        TimeProvider clock)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _envelopeFactory = envelopeFactory ?? throw new ArgumentNullException(nameof(envelopeFactory));
        _ = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    /// <inheritdoc />
    public async Task<OutboxReceipt<TEvent>> EnqueueAsync<TEvent>(
        OutboxEnqueueItem<TEvent> item,
        CancellationToken cancellationToken = default)
        where TEvent : notnull
    {
        ArgumentNullException.ThrowIfNull(item);

        var envelope = await _envelopeFactory.CreateAsync(item, cancellationToken).ConfigureAwait(false);
        var storedEnvelope = await _store.AddAsync(envelope, cancellationToken).ConfigureAwait(false);

        return CreateTypedReceipt<TEvent>(storedEnvelope, item.Message.GetType());
    }

    /// <inheritdoc />
    public async Task<OutboxReceipt> EnqueueAsync(
        OutboxEnqueueItem item,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);

        var envelope = await _envelopeFactory.CreateAsync(item, cancellationToken).ConfigureAwait(false);
        var storedEnvelope = await _store.AddAsync(envelope, cancellationToken).ConfigureAwait(false);

        return CreateReceipt(storedEnvelope, item.MessageType);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<OutboxReceipt<TEvent>>> EnqueueBatchAsync<TEvent>(
        IReadOnlyList<OutboxEnqueueItem<TEvent>> items,
        CancellationToken cancellationToken = default)
        where TEvent : notnull
    {
        ArgumentNullException.ThrowIfNull(items);

        var envelopes = await _envelopeFactory.CreateBatchAsync(items, cancellationToken).ConfigureAwait(false);
        var stored = await _store.AddBatchAsync(envelopes, cancellationToken).ConfigureAwait(false);
        var receipts = new OutboxReceipt<TEvent>[stored.Count];

        for (var index = 0; index < stored.Count; index++)
        {
            receipts[index] = CreateTypedReceipt<TEvent>(stored[index], items[index].Message.GetType());
        }

        return receipts;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<OutboxReceipt>> EnqueueBatchAsync(
        IReadOnlyList<OutboxEnqueueItem> items,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(items);

        var envelopes = await _envelopeFactory.CreateBatchAsync(items, cancellationToken).ConfigureAwait(false);
        var stored = await _store.AddBatchAsync(envelopes, cancellationToken).ConfigureAwait(false);
        var receipts = new OutboxReceipt[stored.Count];

        for (var index = 0; index < stored.Count; index++)
        {
            receipts[index] = CreateReceipt(stored[index], items[index].MessageType);
        }

        return receipts;
    }

    /// <summary>
    ///     Maps a stored envelope to an untyped enqueue receipt.
    /// </summary>
    /// <param name="storedEnvelope">The envelope returned by the store.</param>
    /// <param name="messageType">The runtime message type used for contract lookup.</param>
    /// <returns>The enqueue receipt returned to callers.</returns>
    private static OutboxReceipt CreateReceipt(OutboxEnvelope storedEnvelope, Type messageType)
    {
        return new OutboxReceipt
        {
            Id = storedEnvelope.Id,
            MessageType = messageType,
            Contract = new MessageContractReference
            {
                Name = storedEnvelope.ContractName,
                Version = storedEnvelope.ContractVersion
            },
            StoredAt = storedEnvelope.CreatedAt,
            Trace = DurableEnvelopeMetadataMapper.ResolveTrace(
                storedEnvelope.CorrelationId,
                storedEnvelope.CausationId,
                storedEnvelope.TraceContext),
            Tenant = DurableEnvelopeMetadataMapper.ResolveTenant(storedEnvelope.TenantId)
        };
    }

    /// <summary>
    ///     Maps a stored envelope to a typed enqueue receipt.
    /// </summary>
    /// <typeparam name="TEvent">The compile-time event type associated with the receipt.</typeparam>
    /// <param name="storedEnvelope">The envelope returned by the store.</param>
    /// <param name="messageType">The runtime message type used for contract lookup.</param>
    /// <returns>The typed enqueue receipt returned to callers.</returns>
    private static OutboxReceipt<TEvent> CreateTypedReceipt<TEvent>(OutboxEnvelope storedEnvelope, Type messageType)
        where TEvent : notnull
    {
        var receipt = CreateReceipt(storedEnvelope, messageType);

        return new OutboxReceipt<TEvent>
        {
            Id = receipt.Id,
            MessageType = receipt.MessageType,
            Contract = receipt.Contract,
            StoredAt = receipt.StoredAt,
            Trace = receipt.Trace,
            Tenant = receipt.Tenant
        };
    }
}