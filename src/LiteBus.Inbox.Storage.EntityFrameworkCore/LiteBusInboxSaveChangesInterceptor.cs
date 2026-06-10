using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Inbox.Abstractions;
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
///         envelopes into the current <see cref="IInboxDbContext" /> so the provider writes them in the caller's transaction.
///     </para>
///     <para>
///         Duplicate <c>message_id</c> or <c>idempotency_key</c> conflicts are not resolved idempotently on this path.
///         The provider raises on <c>SaveChanges</c>, which aborts the caller's unit of work (GPT-23).
///     </para>
///     <para>
///         Register the interceptor on the application <see cref="DbContext" /> through
///         <see cref="InboxDbContextExtensions.AddLiteBusInboxInterceptor(DbContextOptionsBuilder, LiteBusInboxSaveChangesInterceptor)" />
///         and enable module registration with
///         <see cref="EfCoreInboxStorageModuleBuilder.EnableSaveChangesInterceptor" />.
///     </para>
/// </remarks>
public sealed class LiteBusInboxSaveChangesInterceptor : SaveChangesInterceptor
{
    /// <summary>
    ///     Holds pending envelopes keyed by the database context that will flush them.
    /// </summary>
    private static readonly ConditionalWeakTable<DbContext, List<InboxEnvelope>> PendingEnvelopesByContext = new();

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

        var pending = PendingEnvelopesByContext.GetValue(context, static _ => []);
        pending.Add(envelope);
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
    private static void FlushPendingEnvelopes(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        if (!PendingEnvelopesByContext.TryGetValue(context, out var pending) || pending.Count == 0)
        {
            return;
        }

        var envelopes = pending
            .GroupBy(envelope => envelope.Id)
            .Select(group => group.First())
            .ToList();
        PendingEnvelopesByContext.Remove(context);

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
            if (trackedIds.Contains(envelope.Id))
            {
                continue;
            }

            inboxDbContext.InboxMessages.Add(ToEntity(envelope));
            trackedIds.Add(envelope.Id);
        }
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
            TenantId = envelope.TenantId,
            TraceContext = envelope.TraceContext
        };
    }
}
