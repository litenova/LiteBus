using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Outbox.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace LiteBus.Outbox.Storage.EntityFrameworkCore;

/// <summary>
///     Enqueues events through <see cref="LiteBusOutboxSaveChangesInterceptor" /> so contract resolution and
///     serialization follow the same path as <see cref="IOutbox" /> while rows commit with the active
///     <see cref="DbContext" /> transaction.
/// </summary>
/// <typeparam name="TContext">The application database context type bound to the current scope.</typeparam>
public sealed class TransactionalOutbox<TContext> : ITransactionalOutbox<TContext>
    where TContext : DbContext
{
    /// <summary>
    ///     Gets the time provider used to stamp storage time on staged envelopes.
    /// </summary>
    private readonly TimeProvider _clock;

    /// <summary>
    ///     Gets the database context that owns the ambient transaction for staged envelopes.
    /// </summary>
    private readonly TContext _dbContext;

    /// <summary>
    ///     Gets the factory used to create envelopes before interceptor staging.
    /// </summary>
    private readonly IOutboxEnvelopeFactory _envelopeFactory;

    /// <summary>
    ///     Gets the interceptor that queues envelopes until the next <c>SaveChanges</c> call.
    /// </summary>
    private readonly LiteBusOutboxSaveChangesInterceptor _interceptor;

    /// <summary>
    ///     Initializes a new instance of the <see cref="TransactionalOutbox{TContext}" /> class.
    /// </summary>
    /// <param name="interceptor">The interceptor that stages envelopes for the active context.</param>
    /// <param name="dbContext">The database context that owns the ambient transaction.</param>
    /// <param name="envelopeFactory">The factory used to create envelopes before interceptor staging.</param>
    /// <param name="clock">The time provider used to stamp storage time on staged envelopes.</param>
    public TransactionalOutbox(
        LiteBusOutboxSaveChangesInterceptor interceptor,
        TContext dbContext,
        IOutboxEnvelopeFactory envelopeFactory,
        TimeProvider clock)
    {
        _interceptor = interceptor ?? throw new ArgumentNullException(nameof(interceptor));
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
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
        _interceptor.Enqueue(_dbContext, envelope);

        return new OutboxReceipt<TEvent>
        {
            Id = envelope.Id,
            MessageType = @event.GetType(),
            ContractName = envelope.ContractName,
            ContractVersion = envelope.ContractVersion,
            StoredAt = envelope.CreatedAt,
            CorrelationId = envelope.CorrelationId,
            CausationId = envelope.CausationId,
            TenantId = envelope.TenantId
        };
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<OutboxReceipt>> EnqueueBatchAsync(
        IReadOnlyList<object> events,
        IReadOnlyList<Type> eventTypes,
        IReadOnlyList<OutboxOptions?>? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(eventTypes);

        if (events.Count != eventTypes.Count)
        {
            throw new ArgumentException("Events and event types must contain the same number of entries.");
        }

        if (options is not null && options.Count != events.Count)
        {
            throw new ArgumentException("Options must contain the same number of entries as events when supplied.");
        }

        if (events.Count == 0)
        {
            return Array.Empty<OutboxReceipt>();
        }

        var envelopes = await _envelopeFactory
            .CreateBatchAsync(events, eventTypes, options, cancellationToken)
            .ConfigureAwait(false);

        var receipts = new OutboxReceipt[events.Count];

        for (var index = 0; index < envelopes.Count; index++)
        {
            var envelope = envelopes[index];
            _interceptor.Enqueue(_dbContext, envelope);

            receipts[index] = new OutboxReceipt
            {
                Id = envelope.Id,
                MessageType = eventTypes[index],
                ContractName = envelope.ContractName,
                ContractVersion = envelope.ContractVersion,
                StoredAt = envelope.CreatedAt,
                CorrelationId = envelope.CorrelationId,
                CausationId = envelope.CausationId,
                TenantId = envelope.TenantId
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
        ArgumentNullException.ThrowIfNull(events);

        if (options is not null && options.Count != events.Count)
        {
            throw new ArgumentException("Options must contain the same number of entries as events when supplied.");
        }

        if (events.Count == 0)
        {
            return Array.Empty<OutboxReceipt<TEvent>>();
        }

        var receipts = new OutboxReceipt<TEvent>[events.Count];

        for (var index = 0; index < events.Count; index++)
        {
            receipts[index] = await EnqueueAsync(events[index], options?[index], cancellationToken).ConfigureAwait(false);
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
