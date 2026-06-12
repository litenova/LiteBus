using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Outbox.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace LiteBus.Outbox.Storage.EntityFrameworkCore;

/// <summary>
///     Queues outbox envelopes and appends them during <see cref="DbContext.SaveChanges()" />.
/// </summary>
/// <remarks>
///     <para>
///         Use this interceptor when application state and outbox rows must commit or roll back together in the same
///         Entity Framework Core transaction.
///     </para>
///     <para>
///         Call <see cref="Enqueue(DbContext, OutboxEnvelope)" /> before <c>SaveChanges</c>. The interceptor copies
///         pending
///         envelopes into the matching <see cref="IOutboxDbContext" /> so the provider writes them in the caller's
///         transaction.
///     </para>
///     <para>
///         Under <see cref="Messaging.Abstractions.DurableMessaging.IdempotencyConflictMode.Strict" />, duplicate
///         <c>message_id</c> or <c>idempotency_key</c> conflicts raise on <c>SaveChanges</c> and abort the caller unit
///         of work. <see cref="TransactionalOutbox{TContext}" /> resolves
///         <see cref="Messaging.Abstractions.DurableMessaging.IdempotencyConflictMode.ReturnExisting" /> before staging.
///     </para>
///     <para>
///         Register the interceptor on the application <see cref="DbContext" /> through
///         <see
///             cref="OutboxDbContextExtensions.AddLiteBusOutboxInterceptor(DbContextOptionsBuilder, LiteBusOutboxSaveChangesInterceptor)" />
///         and enable module registration with
///         <see cref="EfCoreOutboxStorageModuleBuilder.EnableSaveChangesInterceptor" />.
///     </para>
/// </remarks>
public sealed class LiteBusOutboxSaveChangesInterceptor : SaveChangesInterceptor
{
    /// <summary>
    ///     Holds pending envelopes keyed by the <see cref="DbContext" /> that will flush them.
    /// </summary>
    private static readonly ConditionalWeakTable<DbContext, List<OutboxEnvelope>> PendingEnvelopes = new();

    /// <summary>
    ///     Adds an outbox envelope to the pending list flushed by the next <c>SaveChanges</c> call on
    ///     <paramref name="context" />.
    /// </summary>
    /// <param name="context">The context that owns the ambient transaction and will invoke <c>SaveChanges</c>.</param>
    /// <param name="envelope">The envelope to append in the same transaction as <c>SaveChanges</c>.</param>
    public void Enqueue(DbContext context, OutboxEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(envelope);

        var pending = PendingEnvelopes.GetValue(context, static _ => []);
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
    ///     Writes pending envelopes to the outbox set tracked by the current context.
    /// </summary>
    /// <param name="context">The context currently saving changes.</param>
    private static void FlushPendingEnvelopes(DbContext? context)
    {
        if (context is null || !PendingEnvelopes.TryGetValue(context, out var pending) || pending.Count == 0)
        {
            return;
        }

        var envelopes = pending
            .GroupBy(envelope => envelope.Id)
            .Select(group => group.First())
            .ToList();

        PendingEnvelopes.Remove(context);

        if (context is not IOutboxDbContext outboxDbContext)
        {
            throw new InvalidOperationException(
                $"Pending outbox envelopes were queued, but the active context does not implement {nameof(IOutboxDbContext)}.");
        }

        var trackedIds = outboxDbContext.OutboxMessages.Local
            .Select(message => message.Id)
            .ToHashSet();

        foreach (var envelope in envelopes)
        {
            if (trackedIds.Contains(envelope.Id))
            {
                continue;
            }

            outboxDbContext.OutboxMessages.Add(ToEntity(envelope));
            trackedIds.Add(envelope.Id);
        }
    }

    /// <summary>
    ///     Maps an envelope to an Entity Framework Core outbox entity.
    /// </summary>
    /// <param name="envelope">The envelope to map.</param>
    /// <returns>The mapped persistence entity.</returns>
    private static OutboxMessageEntity ToEntity(OutboxEnvelope envelope)
    {
        return new OutboxMessageEntity
        {
            Id = envelope.Id,
            ContractName = envelope.ContractName,
            ContractVersion = envelope.ContractVersion,
            Payload = envelope.Payload,
            Topic = envelope.Topic,
            CreatedAt = envelope.CreatedAt,
            VisibleAfter = envelope.VisibleAfter,
            Status = envelope.Status,
            AttemptCount = envelope.AttemptCount,
            LeaseOwner = envelope.LeaseOwner,
            LeaseExpiresAt = envelope.LeaseExpiresAt,
            LastError = envelope.LastError,
            CorrelationId = envelope.CorrelationId,
            CausationId = envelope.CausationId,
            TenantId = envelope.TenantId,
            IdempotencyKey = envelope.IdempotencyKey,
            TraceContext = envelope.TraceContext
        };
    }
}