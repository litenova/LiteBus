using System;
using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Commands.Recurring;

/// <summary>
///     Decides when the passes of one recurring loop run, per tenant, as wall-clock occurrences.
/// </summary>
/// <remarks>
///     <para>
///         The replaceable port behind <see cref="RecurringCommandHostOptions.TenantSchedule" />: a tenant sweep asks
///         for the next occurrence of one tenant after a supplied instant, and waits for the earliest occurrence
///         across its tenants before each pass. A sweep with a tenant-aware schedule set prefers it and otherwise
///         uses the shared <see cref="RecurringCommandHostOptions.Schedule" />.
///     </para>
///     <para>
///         The shared tenant schedule contract is
///         <see cref="LiteBus.Runtime.Abstractions.Scheduling.ITenantSchedule" />. The tenant <c>DailyAt</c> and
///         <c>WeeklyOn</c> built-ins on <see cref="RecurringSchedules" /> adapt to this port through
///         <see cref="TenantScheduleExtensions.AsRecurringSchedule" />.
///     </para>
///     <para>
///         Implementations must be thread-safe and free of side effects, because the loop calls them on every wait.
///     </para>
/// </remarks>
public interface ITenantRecurringSchedule
{
    /// <summary>
    ///     Gets the next occurrence for one tenant strictly after the supplied instant.
    /// </summary>
    /// <param name="tenantId">The tenant the occurrence is computed for.</param>
    /// <param name="after">The instant the occurrence must follow.</param>
    /// <param name="cancellationToken">A token that stops the lookup.</param>
    /// <returns>The next occurrence, or <see langword="null" /> when the schedule ends.</returns>
    ValueTask<DateTimeOffset?> GetNextOccurrenceAsync(
        string tenantId,
        DateTimeOffset after,
        CancellationToken cancellationToken);
}
