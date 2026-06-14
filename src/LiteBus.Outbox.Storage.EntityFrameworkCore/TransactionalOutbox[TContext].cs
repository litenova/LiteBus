using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions.DurableMessaging;
using LiteBus.Outbox.Abstractions;
using LiteBus.Storage.EntityFrameworkCore.Stores;
using Microsoft.EntityFrameworkCore;

namespace LiteBus.Outbox.Storage.EntityFrameworkCore;

/// <summary>
///     Enqueues events through <see cref="LiteBusOutboxSaveChangesInterceptor" /> so contract resolution and
///     serialization follow the same path as <see cref="IOutbox" /> while rows commit with the active
///     <see cref="DbContext" /> transaction.
/// </summary>
/// <remarks>
///     <para>
///         <see cref="IdempotencyConflictMode.ReturnExisting" /> resolves tracked, pending, and persisted rows by message
///         identifier or tenant-scoped idempotency key before staging. <see cref="IdempotencyConflictMode.Strict" />
///         always stages the new envelope so duplicate scoped keys surface as <see cref="DbUpdateException" /> on
///         <c>SaveChanges</c> and roll back the caller unit of work.
///     </para>
/// </remarks>
/// <typeparam name="TContext">The application database context type bound to the current scope.</typeparam>
public sealed class TransactionalOutbox<TContext> : ITransactionalOutbox<TContext>
    where TContext : DbContext
{
    /// <summary>
    ///     Gets the database context that owns the ambient transaction for staged envelopes.
    /// </summary>
    private readonly TContext _dbContext;

    /// <summary>
    ///     Gets the interceptor that queues envelopes until the next <c>SaveChanges</c> call.
    /// </summary>
    private readonly LiteBusOutboxSaveChangesInterceptor _interceptor;

    /// <summary>
    ///     Gets the shared enqueue pipeline used to create envelopes and map receipts.
    /// </summary>
    private readonly OutboxWriterCore _writerCore;

    /// <summary>
    ///     Initializes a new instance of the <see cref="TransactionalOutbox{TContext}" /> class.
    /// </summary>
    /// <param name="interceptor">The interceptor that stages envelopes for the active context.</param>
    /// <param name="dbContext">The database context that owns the ambient transaction.</param>
    /// <param name="envelopeFactory">The factory used to create envelopes before interceptor staging.</param>
    public TransactionalOutbox(
        LiteBusOutboxSaveChangesInterceptor interceptor,
        TContext dbContext,
        IOutboxEnvelopeFactory envelopeFactory)
    {
        ArgumentNullException.ThrowIfNull(interceptor);
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(envelopeFactory);

        _interceptor = interceptor;
        _dbContext = dbContext;
        _writerCore = new OutboxWriterCore(envelopeFactory);
    }

    /// <inheritdoc />
    public Task<OutboxReceipt<TEvent>> EnqueueAsync<TEvent>(
        OutboxEnqueueItem<TEvent> item,
        CancellationToken cancellationToken = default)
        where TEvent : notnull
    {
        return _writerCore.EnqueueAsync(item, StageAsync, cancellationToken);
    }

    /// <inheritdoc />
    public Task<OutboxReceipt> EnqueueAsync(
        OutboxEnqueueItem item,
        CancellationToken cancellationToken = default)
    {
        return _writerCore.EnqueueAsync(item, StageAsync, cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<OutboxReceipt<TEvent>>> EnqueueBatchAsync<TEvent>(
        IReadOnlyList<OutboxEnqueueItem<TEvent>> items,
        CancellationToken cancellationToken = default)
        where TEvent : notnull
    {
        if (items.Count == 0)
        {
            return Task.FromResult<IReadOnlyList<OutboxReceipt<TEvent>>>([]);
        }

        return _writerCore.EnqueueBatchAsync(
            items,
            async (envelopes, token) =>
            {
                var staged = new OutboxEnvelope[envelopes.Count];

                for (var index = 0; index < envelopes.Count; index++)
                {
                    staged[index] = await StageAsync(envelopes[index], token).ConfigureAwait(false);
                }

                return staged;
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<OutboxReceipt>> EnqueueBatchAsync(
        IReadOnlyList<OutboxEnqueueItem> items,
        CancellationToken cancellationToken = default)
    {
        if (items.Count == 0)
        {
            return Task.FromResult<IReadOnlyList<OutboxReceipt>>([]);
        }

        return _writerCore.EnqueueBatchAsync(
            items,
            async (envelopes, token) =>
            {
                var staged = new OutboxEnvelope[envelopes.Count];

                for (var index = 0; index < envelopes.Count; index++)
                {
                    staged[index] = await StageAsync(envelopes[index], token).ConfigureAwait(false);
                }

                return staged;
            },
            cancellationToken);
    }

    /// <summary>
    ///     Stages one envelope through the save-changes interceptor or returns an existing row for
    ///     <see cref="IdempotencyConflictMode.ReturnExisting" />.
    /// </summary>
    /// <param name="envelope">The envelope created for the current enqueue attempt.</param>
    /// <param name="cancellationToken">The token used to cancel the lookup.</param>
    /// <returns>The envelope staged for persistence or the existing stored envelope.</returns>
    /// <remarks>
    ///     Strict conflict mode never resolves duplicates here; conflicting scoped keys are left for the database unique
    ///     index to reject during <c>SaveChanges</c>.
    /// </remarks>
    private async Task<OutboxEnvelope> StageAsync(OutboxEnvelope envelope, CancellationToken cancellationToken)
    {
        if (envelope.IdempotencyConflictMode == IdempotencyConflictMode.ReturnExisting)
        {
            var existing = await FindExistingAsync(envelope, cancellationToken).ConfigureAwait(false);

            if (existing is not null)
            {
                return existing;
            }
        }

        _interceptor.Enqueue(_dbContext, envelope);
        return envelope;
    }

    /// <summary>
    ///     Finds an existing outbox row matching the attempted message identifier or tenant-scoped idempotency key.
    /// </summary>
    /// <param name="envelope">The envelope created for the current enqueue attempt.</param>
    /// <param name="cancellationToken">The token used to cancel the lookup.</param>
    /// <returns>The existing envelope when one is already tracked or persisted; otherwise <see langword="null" />.</returns>
    private async Task<OutboxEnvelope?> FindExistingAsync(OutboxEnvelope envelope, CancellationToken cancellationToken)
    {
        if (_dbContext is not IOutboxDbContext outboxDbContext)
        {
            return null;
        }

        var local = FindLocalMatch(outboxDbContext, envelope);

        if (local is not null)
        {
            return ToEnvelope(local);
        }

        if (_interceptor.TryFindPending(_dbContext, envelope, out var pending))
        {
            return pending;
        }

        if (string.IsNullOrWhiteSpace(envelope.IdempotencyKey))
        {
            var trackedById = await outboxDbContext.OutboxMessages
                .AsNoTracking()
                .FirstOrDefaultAsync(message => message.Id == envelope.Id, cancellationToken)
                .ConfigureAwait(false);

            return trackedById is null ? null : ToEnvelope(trackedById);
        }

        var normalizedTenantId = EfCoreIdempotencyResolution.NormalizeTenantId(envelope.TenantId);

        var tracked = await outboxDbContext.OutboxMessages
            .AsNoTracking()
            .Where(message =>
                message.Id == envelope.Id ||
                message.IdempotencyKey == envelope.IdempotencyKey &&
                message.TenantId == normalizedTenantId)
            .OrderBy(message => message.Id == envelope.Id ? 0 : 1)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return tracked is null ? null : ToEnvelope(tracked);
    }

    /// <summary>
    ///     Finds a tracked outbox row matching the attempted message identifier or tenant-scoped idempotency key.
    /// </summary>
    /// <param name="outboxDbContext">The outbox database context bound to the current scope.</param>
    /// <param name="envelope">The envelope created for the current enqueue attempt.</param>
    /// <returns>The tracked entity when one matches; otherwise <see langword="null" />.</returns>
    private static OutboxMessageEntity? FindLocalMatch(IOutboxDbContext outboxDbContext, OutboxEnvelope envelope)
    {
        var normalizedTenantId = EfCoreIdempotencyResolution.NormalizeTenantId(envelope.TenantId);

        return outboxDbContext.OutboxMessages.Local.FirstOrDefault(message =>
            message.Id == envelope.Id ||
            !string.IsNullOrWhiteSpace(envelope.IdempotencyKey) &&
            string.Equals(message.IdempotencyKey, envelope.IdempotencyKey, StringComparison.Ordinal) &&
            EfCoreIdempotencyResolution.NormalizeTenantId(message.TenantId) == normalizedTenantId);
    }

    /// <summary>
    ///     Maps a persistence entity to an outbox envelope.
    /// </summary>
    /// <param name="entity">The tracked or queried outbox entity.</param>
    /// <returns>The mapped outbox envelope.</returns>
    private static OutboxEnvelope ToEnvelope(OutboxMessageEntity entity)
    {
        return new OutboxEnvelope
        {
            Id = entity.Id,
            ContractName = entity.ContractName,
            ContractVersion = entity.ContractVersion,
            Payload = entity.Payload,
            Topic = entity.Topic,
            CreatedAt = entity.CreatedAt,
            VisibleAfter = entity.VisibleAfter,
            Status = entity.Status,
            AttemptCount = entity.AttemptCount,
            IdempotencyKey = entity.IdempotencyKey,
            LeaseOwner = entity.LeaseOwner,
            LeaseExpiresAt = entity.LeaseExpiresAt,
            LastError = entity.LastError,
            CorrelationId = entity.CorrelationId,
            CausationId = entity.CausationId,
            TenantId = entity.TenantId,
            TraceContext = entity.TraceContext,
            PublishedAt = entity.PublishedAt
        };
    }
}
