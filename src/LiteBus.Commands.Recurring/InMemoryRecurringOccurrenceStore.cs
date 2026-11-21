using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Commands.Recurring;

/// <summary>
///     Records scheduled occurrences in process memory, so catch-up decisions work without a durable store.
/// </summary>
/// <remarks>
///     <para>
///         The default <see cref="IRecurringOccurrenceStore" />: every recurring module registers it through
///         <c>DependencyDescriptor.ForDefaultPort</c>, and a storage package replaces it with a durable
///         implementation through <c>Replace</c>. A missed occurrence across a restart is best-effort here,
///         because a restart clears what was recorded; a loop that must catch up across crashes uses a durable
///         store.
///     </para>
///     <para>
///         Recorded occurrences are kept for the life of the process and reads are constant-time per loop, so a
///         frequent loop pays one dictionary read per wait.
///     </para>
/// </remarks>
public sealed class InMemoryRecurringOccurrenceStore : IRecurringOccurrenceStore
{
    /// <summary>
    ///     The recorded occurrences keyed by loop, tenant, and occurrence key.
    /// </summary>
    private readonly ConcurrentDictionary<(string LoopName, string? TenantId, string OccurrenceKey), RecurringOccurrence> _occurrences = new();

    /// <summary>
    ///     The most recent recorded occurrence per loop and tenant.
    /// </summary>
    private readonly ConcurrentDictionary<(string LoopName, string? TenantId), RecurringOccurrence> _lastByLoop = new();

    /// <inheritdoc />
    public ValueTask<bool> TryRecordAsync(RecurringOccurrence occurrence, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(occurrence);
        cancellationToken.ThrowIfCancellationRequested();

        if (!_occurrences.TryAdd(
                (occurrence.LoopName, occurrence.TenantId, occurrence.OccurrenceKey),
                occurrence))
        {
            return new ValueTask<bool>(false);
        }

        _lastByLoop.AddOrUpdate(
            (occurrence.LoopName, occurrence.TenantId),
            occurrence,
            (_, existing) => IsLater(occurrence, existing) ? occurrence : existing);

        return new ValueTask<bool>(true);
    }

    /// <inheritdoc />
    public ValueTask<RecurringOccurrence?> GetLastAsync(
        string loopName,
        string? tenantId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(loopName);
        cancellationToken.ThrowIfCancellationRequested();

        _lastByLoop.TryGetValue((loopName, tenantId), out var last);

        return new ValueTask<RecurringOccurrence?>(last);
    }

    /// <summary>
    ///     Determines whether one recorded occurrence is more recent than another.
    /// </summary>
    /// <param name="candidate">The occurrence to compare.</param>
    /// <param name="existing">The occurrence to compare against.</param>
    /// <returns>
    ///     <see langword="true" /> when <paramref name="candidate" /> was recorded later, or was recorded at the
    ///     same instant for a later scheduled occurrence.
    /// </returns>
    private static bool IsLater(RecurringOccurrence candidate, RecurringOccurrence existing)
    {
        return candidate.RecordedAt > existing.RecordedAt
            || (candidate.RecordedAt == existing.RecordedAt && candidate.ScheduledAt > existing.ScheduledAt);
    }
}
