using System;
using System.Collections.Generic;

namespace LiteBus.Inbox.Abstractions;

/// <summary>
///     Describes optional predicates applied when querying or purging inbox messages.
/// </summary>
public sealed record InboxMessageFilter
{
    /// <summary>
    ///     Gets a filter that matches only dead-lettered inbox messages.
    /// </summary>
    public static InboxMessageFilter DeadLettered { get; } = new()
    {
        Statuses = [InboxStatus.DeadLettered]
    };

    /// <summary>
    ///     Gets the statuses that must match when set. When <see langword="null" /> or empty, status is not filtered.
    /// </summary>
    public IReadOnlyList<InboxStatus>? Statuses { get; init; }

    /// <summary>
    ///     Gets the contract name that must match when set.
    /// </summary>
    public string? ContractName { get; init; }

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
    ///     Gets the earliest inclusive <see cref="InboxEnvelope.CreatedAt" /> timestamp when set.
    /// </summary>
    public DateTimeOffset? CreatedAfter { get; init; }

    /// <summary>
    ///     Gets the latest inclusive <see cref="InboxEnvelope.CreatedAt" /> timestamp when set.
    /// </summary>
    public DateTimeOffset? CreatedBefore { get; init; }
}
