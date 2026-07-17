using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Inbox.Abstractions;
using LiteBus.Messaging.Abstractions.DurableMessaging;
using LiteBus.Storage.EntityFrameworkCore.Stores;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace LiteBus.Inbox.Storage.EntityFrameworkCore;

/// <summary>
///     Queues inbox envelopes and appends them during <see cref="DbContext.SaveChanges()" />.
/// </summary>
/// <remarks>
///     <para>
///         Use this interceptor when application state and inbox rows must commit or roll back together in the same
///         Entity Framework Core transaction.
///     </para>
///     <para>
///         Call <see cref="Enqueue(DbContext, InboxEnvelope)" /> before <c>SaveChanges</c>. The interceptor copies pending
///         envelopes into the current <see cref="IInboxDbContext" /> so the provider writes them in the caller's
///         transaction.
///     </para>
///     <para>
///         Under <see cref="IdempotencyConflictMode.Strict" />, duplicate <c>(tenant_id, idempotency_key)</c> pairs are
///         staged without deduplication so the unique index raises <see cref="DbUpdateException" /> on
///         <c>SaveChanges</c> and aborts the caller unit of work. Under
///         <see cref="IdempotencyConflictMode.ReturnExisting" />, <see cref="TransactionalInbox{TContext}" /> resolves
///         existing rows before staging and the flush step skips duplicate scoped keys still present in the pending
///         batch.
///     </para>
///     <para>
///         Register the interceptor on the application <see cref="DbContext" /> through
///         <see
///             cref="InboxDbContextExtensions.AddLiteBusInboxInterceptor(DbContextOptionsBuilder, LiteBusInboxSaveChangesInterceptor)" />
///         and enable module registration with
///         <see cref="EfCoreInboxStorageModuleBuilder.EnableSaveChangesInterceptor" />.
///     </para>
/// </remarks>
public sealed class LiteBusInboxSaveChangesInterceptor : SaveChangesInterceptor
{
    /// <summary>
    ///     Holds pending envelopes keyed by the database context that will flush them.
    /// </summary>
    private readonly ConditionalWeakTable<DbContext, List<InboxEnvelope>> _pendingEnvelopesByContext = new();

    /// <summary>
    ///     Adds an inbox envelope to the pending list flushed by the next <c>SaveChanges</c> call on
    ///     <paramref name="context" />.
    /// </summary>
    /// <param name="context">The database context that owns the ambient transaction.</param>
    /// <param name="envelope">The envelope to append in the same transaction as <c>SaveChanges</c>.</param>
    public void Enqueue(DbContext context, InboxEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(envelope);

        var pending = _pendingEnvelopesByContext.GetValue(context, static _ => []);
        pending.Add(envelope);
    }

    /// <summary>
    ///     Tries to find a pending envelope queued for the same context with a matching message identifier or
    ///     tenant-scoped idempotency key.
    /// </summary>
    /// <param name="context">The database context that owns the ambient transaction.</param>
    /// <param name="envelope">The envelope created for the current accept attempt.</param>
    /// <param name="existing">The pending envelope when one matches the lookup.</param>
    /// <returns><see langword="true" /> when a matching envelope is still pending flush.</returns>
    public bool TryFindPending(DbContext context, InboxEnvelope envelope, out InboxEnvelope existing)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(envelope);
        existing = default!;

        if (!_pendingEnvelopesByContext.TryGetValue(context, out var pending) || pending.Count == 0)
        {
            return false;
        }

        var normalizedTenantId = EfCoreIdempotencyResolution.NormalizeTenantId(envelope.TenantId);

        foreach (var candidate in pending)
        {
            if (candidate.Id == envelope.Id)
            {
                existing = candidate;
                return true;
            }

            if (!string.IsNullOrWhiteSpace(envelope.IdempotencyKey) &&
                string.Equals(candidate.IdempotencyKey, envelope.IdempotencyKey, StringComparison.Ordinal) &&
                EfCoreIdempotencyResolution.NormalizeTenantId(candidate.TenantId) == normalizedTenantId)
            {
                existing = candidate;
                return true;
            }
        }

        return false;
    }

    /// <inheritdoc />
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        FlushPendingEnvelopes(eventData.Context);
        return result;
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        FlushPendingEnvelopes(eventData.Context);
        return new ValueTask<InterceptionResult<int>>(result);
    }

    /// <summary>
    ///     Writes pending envelopes to the inbox set tracked by the current context.
    /// </summary>
    /// <param name="context">The context currently saving changes.</param>
    private void FlushPendingEnvelopes(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        if (!_pendingEnvelopesByContext.TryGetValue(context, out var pending) || pending.Count == 0)
        {
            return;
        }

        var envelopes = PrepareEnvelopesForFlush(pending);

        _pendingEnvelopesByContext.Remove(context);

        if (context is not IInboxDbContext inboxDbContext)
        {
            throw new InvalidOperationException(
                $"Pending inbox envelopes were queued, but the active context does not implement {nameof(IInboxDbContext)}.");
        }

        var trackedIds = inboxDbContext.InboxMessages.Local
            .Select(message => message.Id)
            .ToHashSet();

        foreach (var envelope in envelopes)
        {
            if (ShouldSkipFlush(inboxDbContext, envelope, trackedIds))
            {
                continue;
            }

            inboxDbContext.InboxMessages.Add(ToEntity(envelope));
            trackedIds.Add(envelope.Id);
        }
    }

    /// <summary>
    ///     Collapses duplicate message identifiers and, for <see cref="IdempotencyConflictMode.ReturnExisting" />, duplicate
    ///     tenant-scoped idempotency keys within one pending batch.
    /// </summary>
    /// <param name="pending">The envelopes queued for the current flush.</param>
    /// <returns>The envelopes that should be appended to the change tracker.</returns>
    private static List<InboxEnvelope> PrepareEnvelopesForFlush(List<InboxEnvelope> pending)
    {
        var seenIds = new HashSet<Guid>();
        var seenReturnExistingScopes = new HashSet<string>(StringComparer.Ordinal);
        var envelopes = new List<InboxEnvelope>(pending.Count);

        foreach (var envelope in pending)
        {
            if (!seenIds.Add(envelope.Id))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(envelope.IdempotencyKey))
            {
                envelopes.Add(envelope);
                continue;
            }

            if (envelope.IdempotencyConflictMode == IdempotencyConflictMode.ReturnExisting)
            {
                var scopeKey = EfCoreIdempotencyResolution.CreateScopeKey(envelope.TenantId, envelope.IdempotencyKey);

                if (!seenReturnExistingScopes.Add(scopeKey))
                {
                    continue;
                }
            }

            envelopes.Add(envelope);
        }

        return envelopes;
    }

    /// <summary>
    ///     Determines whether a pending envelope should be skipped because an equivalent row is already tracked.
    /// </summary>
    /// <param name="inboxDbContext">The inbox database context currently saving changes.</param>
    /// <param name="envelope">The pending envelope under consideration.</param>
    /// <param name="trackedIds">The message identifiers already tracked on the context.</param>
    /// <returns><see langword="true" /> when the envelope should not be appended again.</returns>
    private static bool ShouldSkipFlush(
        IInboxDbContext inboxDbContext,
        InboxEnvelope envelope,
        HashSet<Guid> trackedIds)
    {
        if (trackedIds.Contains(envelope.Id))
        {
            return true;
        }

        if (envelope.IdempotencyConflictMode != IdempotencyConflictMode.ReturnExisting ||
            string.IsNullOrWhiteSpace(envelope.IdempotencyKey))
        {
            return false;
        }

        var normalizedTenantId = EfCoreIdempotencyResolution.NormalizeTenantId(envelope.TenantId);

        return inboxDbContext.InboxMessages.Local.Any(message =>
            string.Equals(message.IdempotencyKey, envelope.IdempotencyKey, StringComparison.Ordinal) &&
            EfCoreIdempotencyResolution.NormalizeTenantId(message.TenantId) == normalizedTenantId);
    }

    /// <summary>
    ///     Maps an envelope to an Entity Framework Core inbox entity.
    /// </summary>
    /// <param name="envelope">The envelope to map.</param>
    /// <returns>The mapped persistence entity.</returns>
    private static InboxMessageEntity ToEntity(InboxEnvelope envelope)
    {
        return new InboxMessageEntity
        {
            Id = envelope.Id,
            ContractName = envelope.ContractName,
            ContractVersion = envelope.ContractVersion,
            Payload = envelope.Payload,
            CreatedAt = envelope.CreatedAt,
            VisibleAfter = envelope.VisibleAfter,
            Status = envelope.Status,
            AttemptCount = envelope.AttemptCount,
            LeaseOwner = envelope.LeaseOwner,
            LeaseExpiresAt = envelope.LeaseExpiresAt,
            LastError = envelope.LastError,
            IdempotencyKey = envelope.IdempotencyKey,
            CorrelationId = envelope.CorrelationId,
            CausationId = envelope.CausationId,
            TenantId = EfCoreIdempotencyResolution.NormalizeTenantId(envelope.TenantId),
            TraceContext = envelope.TraceContext
        };
    }
}
