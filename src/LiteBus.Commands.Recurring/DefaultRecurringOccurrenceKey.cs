using System;
using System.Globalization;

namespace LiteBus.Commands.Recurring;

/// <summary>
///     Builds occurrence keys from the loop name, the tenant when there is one, and the scheduled instant in UTC.
/// </summary>
/// <remarks>
///     A loop without tenants keys by <c>loop/20260105T090000Z</c>; a tenant sweep keys by
///     <c>loop/tenant/20260105T090000Z</c>. The instant renders in UTC so replicas in different zones record the same
///     key, in a compact sortable form so occurrence keys stay readable inside lease names.
/// </remarks>
public sealed class DefaultRecurringOccurrenceKey : IRecurringOccurrenceKey
{
    /// <summary>
    ///     Gets the shared default occurrence key builder.
    /// </summary>
    public static DefaultRecurringOccurrenceKey Instance { get; } = new();

    /// <inheritdoc />
    public string CreateKey(string loopName, string? tenantId, DateTimeOffset scheduledAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(loopName);

        var instant = scheduledAt.UtcDateTime.ToString("yyyyMMddTHHmmssZ", CultureInfo.InvariantCulture);

        return string.IsNullOrWhiteSpace(tenantId)
            ? $"{loopName}/{instant}"
            : $"{loopName}/{tenantId}/{instant}";
    }
}
