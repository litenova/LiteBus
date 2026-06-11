using System;

namespace LiteBus.Outbox.Abstractions;

/// <summary>
///     Helper methods for <see cref="OutboxMessageFilter" /> used by operator purge safety checks.
/// </summary>
public static class OutboxMessageFilterExtensions
{
    /// <summary>
    ///     Returns <see langword="true" /> when the filter would match every stored row.
    /// </summary>
    /// <param name="filter">The filter to inspect.</param>
    /// <returns><see langword="true" /> when no predicate narrows the result set.</returns>
    public static bool IsUnrestricted(this OutboxMessageFilter filter)
    {
        ArgumentNullException.ThrowIfNull(filter);

        return !filter.HasMinimumCriteria();
    }

    /// <summary>
    ///     Returns <see langword="true" /> when at least one predicate narrows the matched rows.
    /// </summary>
    /// <param name="filter">The filter to inspect.</param>
    /// <returns><see langword="true" /> when purge or query is scoped to a subset of rows.</returns>
    public static bool HasMinimumCriteria(this OutboxMessageFilter filter)
    {
        ArgumentNullException.ThrowIfNull(filter);

        if (filter.MessageId is not null)
        {
            return true;
        }

        if (filter.MessageIds is { Count: > 0 })
        {
            return true;
        }

        if (filter.Statuses is { Count: > 0 })
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(filter.ContractName))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(filter.Topic))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(filter.CorrelationId))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(filter.CausationId))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(filter.TenantId))
        {
            return true;
        }

        if (filter.CreatedAfter is not null || filter.CreatedBefore is not null)
        {
            return true;
        }

        return false;
    }
}