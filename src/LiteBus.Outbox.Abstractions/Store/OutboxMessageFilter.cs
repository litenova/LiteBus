using System;
using System.Collections.Generic;

namespace LiteBus.Outbox.Abstractions;

/// <summary>
///     Describes optional predicates applied when querying or purging outbox messages.
/// </summary>
public sealed record OutboxMessageFilter
{
    /// <summary>
    ///     Gets a filter that matches only dead-lettered outbox messages.
    /// </summary>
    public static OutboxMessageFilter DeadLettered { get; } = new()
    {
        Statuses = [OutboxStatus.DeadLettered]
    };

    /// <summary>
    ///     Gets the message identifier that must match when set.
    /// </summary>
    public Guid? MessageId { get; init; }

    /// <summary>
    ///     Gets the message identifiers that must match when set.
    /// </summary>
    public IReadOnlyList<Guid>? MessageIds { get; init; }

    /// <summary>
    ///     Gets the statuses that must match when set. When <see langword="null" /> or empty, status is not filtered.
    /// </summary>
    public IReadOnlyList<OutboxStatus>? Statuses { get; init; }

    /// <summary>
    ///     Gets the contract name that must match when set.
    /// </summary>
    public string? ContractName { get; init; }

    /// <summary>
    ///     Gets the topic that must match when set.
    /// </summary>
    public string? Topic { get; init; }

    /// <summary>
    ///     Gets the correlation identifier that must match when set.
    /// </summary>
    public string? CorrelationId { get; init; }

    /// <summary>
    ///     Gets the causation identifier that must match when set.
    /// </summary>
    public string? CausationId { get; init; }

    /// <summary>
    ///     Gets the tenant identifier that must match when set.
    /// </summary>
    public string? TenantId { get; init; }

    /// <summary>
    ///     Gets the earliest inclusive <see cref="OutboxEnvelope.CreatedAt" /> timestamp when set.
    /// </summary>
    public DateTimeOffset? CreatedAfter { get; init; }

    /// <summary>
    ///     Gets the latest inclusive <see cref="OutboxEnvelope.CreatedAt" /> timestamp when set.
    /// </summary>
    public DateTimeOffset? CreatedBefore { get; init; }
}