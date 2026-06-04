using System;
using System.Collections.Generic;
using System.Linq;
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
///         Call <see cref="Enqueue(OutboxEnvelope)" /> before <c>SaveChanges</c>. The interceptor copies pending envelopes
///         into the current <see cref="IOutboxDbContext" /> so the provider writes them in the caller's transaction.
///     </para>
///     <para>
///         Register the interceptor on the application <see cref="DbContext" /> through
///         <see cref="OutboxDbContextExtensions.AddLiteBusOutboxInterceptor(DbContextOptionsBuilder, LiteBusOutboxSaveChangesInterceptor)" />
///         and enable module registration with
///         <see cref="EfCoreOutboxStorageModuleBuilder.EnableSaveChangesInterceptor" />.
///     </para>
/// </remarks>
public sealed class LiteBusOutboxSaveChangesInterceptor : SaveChangesInterceptor
{
    /// <summary>
    ///     Holds pending envelopes for the current asynchronous flow.
    /// </summary>
    private static readonly AsyncLocal<List<OutboxEnvelope>?> PendingEnvelopes = new();

    /// <summary>
    ///     Adds an outbox envelope to the pending list flushed by the next <c>SaveChanges</c> call.
    /// </summary>
    /// <param name="envelope">The envelope to append in the same transaction as <c>SaveChanges</c>.</param>
    public void Enqueue(OutboxEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        var pending = PendingEnvelopes.Value;
        if (pending is null)
        {
            pending = [];
            PendingEnvelopes.Value = pending;
        }

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
        var pending = PendingEnvelopes.Value;
        if (pending is null || pending.Count == 0)
        {
            return;
        }

        if (context is not IOutboxDbContext outboxDbContext)
        {
            throw new InvalidOperationException(
                $"Pending outbox envelopes were queued, but the active context does not implement {nameof(IOutboxDbContext)}.");
        }

        PendingEnvelopes.Value = null;

        var trackedIds = outboxDbContext.OutboxMessages.Local
            .Select(message => message.Id)
            .ToHashSet();

        foreach (var envelope in pending)
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
            TenantId = envelope.TenantId
        };
    }
}
