using System;
using LiteBus.Messaging.Abstractions.DurableMessaging;

namespace LiteBus.Messaging;

/// <summary>
///     Projects durable metadata value objects onto envelope column values shared by inbox and outbox writers.
/// </summary>
internal static class DurableEnvelopeMetadataMapper
{
    /// <summary>
    ///     Resolves the message identifier stored with an envelope from identity metadata.
    /// </summary>
    /// <param name="identity">The identity metadata supplied by the caller.</param>
    /// <returns>The message identifier to persist with the envelope.</returns>
    internal static Guid ResolveMessageId(MessageIdentity identity)
    {
        return identity switch
        {
            MessageIdentity.Supplied supplied => supplied.Value,
            _                                 => Guid.NewGuid()
        };
    }

    /// <summary>
    ///     Resolves the earliest UTC timestamp at which an envelope may be leased.
    /// </summary>
    /// <param name="visibility">The visibility metadata supplied by the caller.</param>
    /// <param name="clock">The time provider used to resolve relative delays.</param>
    /// <returns>The visible-after timestamp, or <see langword="null" /> when the message is due immediately.</returns>
    internal static DateTimeOffset? ResolveVisibleAfter(MessageVisibility visibility, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        return visibility switch
        {
            MessageVisibility.At at => at.VisibleAfter,
            MessageVisibility.After after =>
                clock.GetUtcNow().Add(after.Delay),
            _ => null
        };
    }

    /// <summary>
    ///     Resolves the idempotency key column value from idempotency metadata.
    /// </summary>
    /// <param name="idempotency">The idempotency metadata supplied by the caller.</param>
    /// <returns>The idempotency key to persist, or <see langword="null" /> when duplicate detection is not requested.</returns>
    internal static string? ResolveIdempotencyKey(Idempotency idempotency)
    {
        return idempotency switch
        {
            Idempotency.Keyed keyed when !string.IsNullOrWhiteSpace(keyed.Key) => keyed.Key,
            _                                                                  => null
        };
    }

    /// <summary>
    ///     Resolves how duplicate idempotency keys should be handled for one accept or enqueue attempt.
    /// </summary>
    /// <param name="idempotency">The idempotency metadata supplied by the caller.</param>
    /// <returns>The conflict mode applied by writers and stores for the attempt.</returns>
    internal static IdempotencyConflictMode ResolveIdempotencyConflictMode(Idempotency idempotency)
    {
        return idempotency switch
        {
            Idempotency.Keyed keyed => keyed.ConflictMode,
            _                       => IdempotencyConflictMode.ReturnExisting
        };
    }


    /// <summary>
    ///     Resolves correlation, causation, and trace context column values from trace metadata.
    /// </summary>
    /// <param name="trace">The trace metadata supplied by the caller.</param>
    /// <returns>The envelope trace column values.</returns>
    internal static (string? CorrelationId, string? CausationId, string? TraceContext) ResolveTraceColumns(
        MessageTrace trace)
    {
        return trace switch
        {
            MessageTrace.Correlated correlated => (correlated.CorrelationId, null, null),
            MessageTrace.Workflow workflow     => (workflow.CorrelationId, workflow.CausationId, null),
            MessageTrace.Distributed distributed => (
                distributed.CorrelationId,
                distributed.CausationId,
                distributed.TraceContext),
            _ => (null, null, null)
        };
    }

    /// <summary>
    ///     Resolves the tenant identifier column value from tenant metadata.
    /// </summary>
    /// <param name="tenant">The tenant metadata supplied by the caller.</param>
    /// <returns>The tenant identifier to persist, or <see langword="null" /> when unscoped.</returns>
    internal static string? ResolveTenantId(TenantScope tenant)
    {
        return tenant switch
        {
            TenantScope.Isolated isolated when !string.IsNullOrWhiteSpace(isolated.TenantId) => isolated.TenantId,
            _                                                                                => null
        };
    }

    /// <summary>
    ///     Reconstructs trace metadata from persisted envelope columns.
    /// </summary>
    /// <param name="correlationId">The optional correlation identifier stored with the envelope.</param>
    /// <param name="causationId">The optional causation identifier stored with the envelope.</param>
    /// <param name="traceContext">The optional distributed trace context stored with the envelope.</param>
    /// <returns>The trace metadata represented by the stored columns.</returns>
    internal static MessageTrace ResolveTrace(
        string? correlationId,
        string? causationId,
        string? traceContext)
    {
        if (!string.IsNullOrWhiteSpace(traceContext) && !string.IsNullOrWhiteSpace(correlationId) && !string.IsNullOrWhiteSpace(causationId))
        {
            return new MessageTrace.Distributed(correlationId, causationId, traceContext);
        }

        if (!string.IsNullOrWhiteSpace(correlationId) && !string.IsNullOrWhiteSpace(causationId))
        {
            return new MessageTrace.Workflow(correlationId, causationId);
        }

        if (!string.IsNullOrWhiteSpace(correlationId))
        {
            return new MessageTrace.Correlated(correlationId);
        }

        return MessageTrace.None.Instance;
    }

    /// <summary>
    ///     Reconstructs tenant metadata from the persisted tenant identifier column.
    /// </summary>
    /// <param name="tenantId">The optional tenant identifier stored with the envelope.</param>
    /// <returns>The tenant metadata represented by the stored column.</returns>
    internal static TenantScope ResolveTenant(string? tenantId)
    {
        return string.IsNullOrWhiteSpace(tenantId)
            ? TenantScope.Unscoped.Instance
            : new TenantScope.Isolated(tenantId);
    }
}