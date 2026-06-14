using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Inbox.Abstractions;
using LiteBus.Messaging.Abstractions.DurableMessaging;
using LiteBus.Storage.EntityFrameworkCore.Stores;
using Microsoft.EntityFrameworkCore;

namespace LiteBus.Inbox.Storage.EntityFrameworkCore;

/// <summary>
///     Accepts messages through <see cref="LiteBusInboxSaveChangesInterceptor" /> so contract resolution and
///     serialization follow the same path as <see cref="IInbox" /> while rows commit with the active
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
public sealed class TransactionalInbox<TContext> : ITransactionalInbox<TContext>
    where TContext : DbContext
{
    /// <summary>
    ///     Gets the shared acceptance pipeline used to create envelopes and map receipts.
    /// </summary>
    private readonly InboxAcceptanceService _acceptanceService;

    /// <summary>
    ///     Gets the database context that owns the ambient transaction for staged envelopes.
    /// </summary>
    private readonly TContext _dbContext;

    /// <summary>
    ///     Gets the interceptor that queues envelopes until the next <c>SaveChanges</c> call.
    /// </summary>
    private readonly LiteBusInboxSaveChangesInterceptor _interceptor;

    /// <summary>
    ///     Initializes a new instance of the <see cref="TransactionalInbox{TContext}" /> class.
    /// </summary>
    /// <param name="interceptor">The interceptor that stages envelopes for the active context.</param>
    /// <param name="dbContext">The database context that owns the ambient transaction.</param>
    /// <param name="envelopeFactory">The factory used to create envelopes before interceptor staging.</param>
    public TransactionalInbox(
        LiteBusInboxSaveChangesInterceptor interceptor,
        TContext dbContext,
        IInboxEnvelopeFactory envelopeFactory)
    {
        ArgumentNullException.ThrowIfNull(interceptor);
        _interceptor = interceptor;
        ArgumentNullException.ThrowIfNull(dbContext);
        _dbContext = dbContext;
        ArgumentNullException.ThrowIfNull(envelopeFactory);
        _acceptanceService = new InboxAcceptanceService(envelopeFactory);
    }

    /// <inheritdoc />
    public Task<InboxReceipt<TMessage>> AcceptAsync<TMessage>(
        InboxAcceptItem<TMessage> item,
        CancellationToken cancellationToken = default)
        where TMessage : notnull
    {
        return _acceptanceService.AcceptAsync(item, StageAsync, cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<InboxReceipt>> AcceptBatchAsync(
        IReadOnlyList<InboxAcceptItem> items,
        CancellationToken cancellationToken = default)
    {
        if (items.Count == 0)
        {
            return Task.FromResult<IReadOnlyList<InboxReceipt>>([]);
        }

        return _acceptanceService.AcceptBatchAsync(
            items,
            async (envelopes, token) =>
            {
                var staged = new InboxEnvelope[envelopes.Count];

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
    /// <param name="envelope">The envelope created for the current accept attempt.</param>
    /// <param name="cancellationToken">The token used to cancel the lookup.</param>
    /// <returns>The envelope staged for persistence or the existing stored envelope.</returns>
    /// <remarks>
    ///     Strict conflict mode never resolves duplicates here; conflicting scoped keys are left for the database unique
    ///     index to reject during <c>SaveChanges</c>.
    /// </remarks>
    private async Task<InboxEnvelope> StageAsync(InboxEnvelope envelope, CancellationToken cancellationToken)
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
    ///     Finds an existing inbox row matching the attempted message identifier or tenant-scoped idempotency key.
    /// </summary>
    /// <param name="envelope">The envelope created for the current accept attempt.</param>
    /// <param name="cancellationToken">The token used to cancel the lookup.</param>
    /// <returns>The existing envelope when one is already tracked or persisted; otherwise <see langword="null" />.</returns>
    private async Task<InboxEnvelope?> FindExistingAsync(InboxEnvelope envelope, CancellationToken cancellationToken)
    {
        if (_dbContext is not IInboxDbContext inboxDbContext)
        {
            return null;
        }

        var local = FindLocalMatch(inboxDbContext, envelope);

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
            var trackedById = await inboxDbContext.InboxMessages
                .AsNoTracking()
                .FirstOrDefaultAsync(message => message.Id == envelope.Id, cancellationToken)
                .ConfigureAwait(false);

            return trackedById is null ? null : ToEnvelope(trackedById);
        }

        var normalizedTenantId = EfCoreIdempotencyResolution.NormalizeTenantId(envelope.TenantId);

        var tracked = await inboxDbContext.InboxMessages
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
    ///     Finds a tracked inbox row matching the attempted message identifier or tenant-scoped idempotency key.
    /// </summary>
    /// <param name="inboxDbContext">The inbox database context bound to the current scope.</param>
    /// <param name="envelope">The envelope created for the current accept attempt.</param>
    /// <returns>The tracked entity when one matches; otherwise <see langword="null" />.</returns>
    private static InboxMessageEntity? FindLocalMatch(IInboxDbContext inboxDbContext, InboxEnvelope envelope)
    {
        var normalizedTenantId = EfCoreIdempotencyResolution.NormalizeTenantId(envelope.TenantId);

        return inboxDbContext.InboxMessages.Local.FirstOrDefault(message =>
            message.Id == envelope.Id ||
            !string.IsNullOrWhiteSpace(envelope.IdempotencyKey) &&
            string.Equals(message.IdempotencyKey, envelope.IdempotencyKey, StringComparison.Ordinal) &&
            EfCoreIdempotencyResolution.NormalizeTenantId(message.TenantId) == normalizedTenantId);
    }

    /// <summary>
    ///     Maps a persistence entity to an inbox envelope.
    /// </summary>
    /// <param name="entity">The tracked or queried inbox entity.</param>
    /// <returns>The mapped inbox envelope.</returns>
    private static InboxEnvelope ToEnvelope(InboxMessageEntity entity)
    {
        return new InboxEnvelope
        {
            Id = entity.Id,
            ContractName = entity.ContractName,
            ContractVersion = entity.ContractVersion,
            Payload = entity.Payload,
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
            CompletedAt = entity.CompletedAt
        };
    }
}
