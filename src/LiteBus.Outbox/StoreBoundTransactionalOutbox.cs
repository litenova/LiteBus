using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Outbox.Abstractions;

namespace LiteBus.Outbox;

/// <summary>
///     Enqueues events through a bound <see cref="ITransactionalOutboxStore" /> without committing the caller transaction.
/// </summary>
public sealed class StoreBoundTransactionalOutbox : ITransactionalOutbox
{
    /// <summary>
    ///     Gets the bound outbox store participating in the caller transaction.
    /// </summary>
    private readonly ITransactionalOutboxStore _store;

    /// <summary>
    ///     Gets the factory used to create envelopes before store writes.
    /// </summary>
    private readonly IOutboxEnvelopeFactory _envelopeFactory;

    /// <summary>
    ///     Gets the time provider used for scheduled enqueue timestamps.
    /// </summary>
    private readonly TimeProvider _clock;

    /// <summary>
    ///     Initializes a new instance of the <see cref="StoreBoundTransactionalOutbox" /> class.
    /// </summary>
    /// <param name="store">The bound outbox store participating in the caller transaction.</param>
    /// <param name="envelopeFactory">The factory used to create envelopes before store writes.</param>
    /// <param name="clock">The time provider used for scheduled enqueue timestamps.</param>
    public StoreBoundTransactionalOutbox(
        ITransactionalOutboxStore store,
        IOutboxEnvelopeFactory envelopeFactory,
        TimeProvider clock)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _envelopeFactory = envelopeFactory ?? throw new ArgumentNullException(nameof(envelopeFactory));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    /// <inheritdoc />
    public async Task<OutboxReceipt<TEvent>> EnqueueAsync<TEvent>(
        TEvent @event,
        OutboxOptions? options = null,
        CancellationToken cancellationToken = default)
        where TEvent : notnull
    {
        var envelope = await _envelopeFactory.CreateAsync(@event, options, cancellationToken).ConfigureAwait(false);
        var storedEnvelope = await _store.AddAsync(envelope, cancellationToken).ConfigureAwait(false);

        return new OutboxReceipt<TEvent>
        {
            Id = storedEnvelope.Id,
            MessageType = @event.GetType(),
            ContractName = storedEnvelope.ContractName,
            ContractVersion = storedEnvelope.ContractVersion,
            StoredAt = storedEnvelope.CreatedAt,
            CorrelationId = storedEnvelope.CorrelationId,
            CausationId = storedEnvelope.CausationId,
            TenantId = storedEnvelope.TenantId
        };
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<OutboxReceipt>> EnqueueBatchAsync(
        IReadOnlyList<object> events,
        IReadOnlyList<Type> eventTypes,
        IReadOnlyList<OutboxOptions?>? options = null,
        CancellationToken cancellationToken = default)
    {
        var envelopes = await _envelopeFactory
            .CreateBatchAsync(events, eventTypes, options, cancellationToken)
            .ConfigureAwait(false);
        var stored = await _store.AddBatchAsync(envelopes, cancellationToken).ConfigureAwait(false);
        var receipts = new OutboxReceipt[stored.Count];

        for (var index = 0; index < stored.Count; index++)
        {
            var storedEnvelope = stored[index];
            receipts[index] = new OutboxReceipt
            {
                Id = storedEnvelope.Id,
                MessageType = eventTypes[index],
                ContractName = storedEnvelope.ContractName,
                ContractVersion = storedEnvelope.ContractVersion,
                StoredAt = storedEnvelope.CreatedAt,
                CorrelationId = storedEnvelope.CorrelationId,
                CausationId = storedEnvelope.CausationId,
                TenantId = storedEnvelope.TenantId
            };
        }

        return receipts;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<OutboxReceipt<TEvent>>> EnqueueBatchAsync<TEvent>(
        IReadOnlyList<TEvent> events,
        IReadOnlyList<OutboxOptions?>? options = null,
        CancellationToken cancellationToken = default)
        where TEvent : notnull
    {
        var envelopes = await _envelopeFactory.CreateBatchAsync(events, options, cancellationToken).ConfigureAwait(false);
        var stored = await _store.AddBatchAsync(envelopes, cancellationToken).ConfigureAwait(false);
        var receipts = new OutboxReceipt<TEvent>[stored.Count];

        for (var index = 0; index < stored.Count; index++)
        {
            var storedEnvelope = stored[index];
            receipts[index] = new OutboxReceipt<TEvent>
            {
                Id = storedEnvelope.Id,
                MessageType = events[index].GetType(),
                ContractName = storedEnvelope.ContractName,
                ContractVersion = storedEnvelope.ContractVersion,
                StoredAt = storedEnvelope.CreatedAt,
                CorrelationId = storedEnvelope.CorrelationId,
                CausationId = storedEnvelope.CausationId,
                TenantId = storedEnvelope.TenantId
            };
        }

        return receipts;
    }

    /// <inheritdoc />
    public Task<OutboxReceipt<TEvent>> ScheduleAsync<TEvent>(
        TEvent @event,
        DateTimeOffset enqueueAt,
        OutboxOptions? options = null,
        CancellationToken cancellationToken = default)
        where TEvent : notnull
    {
        return EnqueueAsync(@event, WithVisibleAfter(options, enqueueAt), cancellationToken);
    }

    /// <inheritdoc />
    public Task<OutboxReceipt<TEvent>> ScheduleAfterAsync<TEvent>(
        TEvent @event,
        TimeSpan delay,
        OutboxOptions? options = null,
        CancellationToken cancellationToken = default)
        where TEvent : notnull
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(delay, TimeSpan.Zero, nameof(delay));

        return EnqueueAsync(@event, WithVisibleAfter(options, _clock.GetUtcNow().Add(delay)), cancellationToken);
    }

    /// <summary>
    ///     Merges the supplied options with a scheduled visibility timestamp.
    /// </summary>
    /// <param name="options">The caller-supplied outbox options, if any.</param>
    /// <param name="visibleAfter">The UTC timestamp when the event becomes visible to processors.</param>
    /// <returns>Outbox options with <see cref="OutboxOptions.VisibleAfter" /> set.</returns>
    private static OutboxOptions WithVisibleAfter(OutboxOptions? options, DateTimeOffset visibleAfter)
    {
        options ??= new OutboxOptions();

        return options with { VisibleAfter = visibleAfter };
    }
}
