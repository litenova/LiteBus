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
    ///     Gets the bound inbox store participating in the caller transaction.
    /// </summary>
    private readonly ITransactionalInboxStore _store;

    /// <summary>
    ///     Gets the factory used to create envelopes before store writes.
    /// </summary>
    private readonly IInboxEnvelopeFactory _envelopeFactory;

    /// <summary>
    ///     Gets the time provider used for scheduled acceptance timestamps.
    /// </summary>
    private readonly TimeProvider _clock;

    /// <summary>
    ///     Initializes a new instance of the <see cref="StoreBoundTransactionalInbox" /> class.
    /// </summary>
    /// <param name="store">The bound inbox store participating in the caller transaction.</param>
    /// <param name="envelopeFactory">The factory used to create envelopes before store writes.</param>
    /// <param name="clock">The time provider used for scheduled acceptance timestamps.</param>
    public StoreBoundTransactionalInbox(
        ITransactionalInboxStore store,
        IInboxEnvelopeFactory envelopeFactory,
        TimeProvider clock)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _envelopeFactory = envelopeFactory ?? throw new ArgumentNullException(nameof(envelopeFactory));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    /// <inheritdoc />
    public async Task<InboxReceipt> AcceptAsync(
        object message,
        Type messageType,
        InboxOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var envelope = await _envelopeFactory.CreateAsync(message, messageType, options, cancellationToken)
            .ConfigureAwait(false);
        var storedEnvelope = await _store.AddAsync(envelope, cancellationToken).ConfigureAwait(false);
        return CreateReceipt(storedEnvelope, messageType);
    }

    /// <inheritdoc />
    public async Task<InboxReceipt<T>> AcceptAsync<T>(
        T message,
        InboxOptions? options = null,
        CancellationToken cancellationToken = default)
        where T : notnull
    {
        var receipt = await AcceptAsync(message, message.GetType(), options, cancellationToken).ConfigureAwait(false);

        return new InboxReceipt<T>
        {
            Id = receipt.Id,
            MessageType = receipt.MessageType,
            ContractName = receipt.ContractName,
            ContractVersion = receipt.ContractVersion,
            AcceptedAt = receipt.AcceptedAt,
            CorrelationId = receipt.CorrelationId,
            CausationId = receipt.CausationId,
            TenantId = receipt.TenantId
        };
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<InboxReceipt>> AcceptBatchAsync(
        IReadOnlyList<object> messages,
        IReadOnlyList<Type> messageTypes,
        IReadOnlyList<InboxOptions?>? options = null,
        CancellationToken cancellationToken = default)
    {
        var envelopes = await _envelopeFactory
            .CreateBatchAsync(messages, messageTypes, options, cancellationToken)
            .ConfigureAwait(false);
        var stored = await _store.AddBatchAsync(envelopes, cancellationToken).ConfigureAwait(false);
        var receipts = new InboxReceipt[stored.Count];

        for (var index = 0; index < stored.Count; index++)
        {
            receipts[index] = CreateReceipt(stored[index], messageTypes[index]);
        }

        return receipts;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<InboxReceipt<T>>> AcceptBatchAsync<T>(
        IReadOnlyList<T> messages,
        IReadOnlyList<InboxOptions?>? options = null,
        CancellationToken cancellationToken = default)
        where T : notnull
    {
        var envelopes = await _envelopeFactory.CreateBatchAsync(messages, options, cancellationToken).ConfigureAwait(false);
        var stored = await _store.AddBatchAsync(envelopes, cancellationToken).ConfigureAwait(false);
        var receipts = new InboxReceipt<T>[stored.Count];

        for (var index = 0; index < stored.Count; index++)
        {
            var storedEnvelope = stored[index];
            receipts[index] = new InboxReceipt<T>
            {
                Id = storedEnvelope.Id,
                MessageType = messages[index].GetType(),
                ContractName = storedEnvelope.ContractName,
                ContractVersion = storedEnvelope.ContractVersion,
                AcceptedAt = storedEnvelope.CreatedAt,
                CorrelationId = storedEnvelope.CorrelationId,
                CausationId = storedEnvelope.CausationId,
                TenantId = storedEnvelope.TenantId
            };
        }

        return receipts;
    }

    /// <inheritdoc />
    public Task<InboxReceipt<T>> ScheduleAsync<T>(
        T message,
        DateTimeOffset enqueueAt,
        InboxOptions? options = null,
        CancellationToken cancellationToken = default)
        where T : notnull
    {
        return AcceptAsync(message, WithVisibleAfter(options, enqueueAt), cancellationToken);
    }

    /// <inheritdoc />
    public Task<InboxReceipt<T>> ScheduleAfterAsync<T>(
        T message,
        TimeSpan delay,
        InboxOptions? options = null,
        CancellationToken cancellationToken = default)
        where T : notnull
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(delay, TimeSpan.Zero, nameof(delay));

        return AcceptAsync(message, WithVisibleAfter(options, _clock.GetUtcNow().Add(delay)), cancellationToken);
    }

    /// <summary>
    ///     Maps a stored envelope to an acceptance receipt.
    /// </summary>
    /// <param name="storedEnvelope">The envelope returned by the bound store.</param>
    /// <param name="messageType">The runtime message type used for contract lookup.</param>
    /// <returns>The acceptance receipt returned to callers.</returns>
    private static InboxReceipt CreateReceipt(InboxEnvelope storedEnvelope, Type messageType) =>
        new()
        {
            Id = storedEnvelope.Id,
            MessageType = messageType,
            ContractName = storedEnvelope.ContractName,
            ContractVersion = storedEnvelope.ContractVersion,
            AcceptedAt = storedEnvelope.CreatedAt,
            CorrelationId = storedEnvelope.CorrelationId,
            CausationId = storedEnvelope.CausationId,
            TenantId = storedEnvelope.TenantId
        };

    /// <summary>
    ///     Merges the supplied options with a scheduled visibility timestamp.
    /// </summary>
    /// <param name="options">The caller-supplied inbox options, if any.</param>
    /// <param name="visibleAfter">The UTC timestamp when the message becomes visible to processors.</param>
    /// <returns>Inbox options with <see cref="InboxOptions.VisibleAfter" /> set.</returns>
    private static InboxOptions WithVisibleAfter(InboxOptions? options, DateTimeOffset visibleAfter)
    {
        options ??= new InboxOptions();

        return options with { VisibleAfter = visibleAfter };
    }
}
