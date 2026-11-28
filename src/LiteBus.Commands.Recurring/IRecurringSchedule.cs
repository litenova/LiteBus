using System;
using LiteBus.Runtime.Abstractions.Scheduling;

namespace LiteBus.Commands.Recurring;

/// <summary>
///     Decides when the passes of one recurring loop run, as wall-clock occurrences.
/// </summary>
/// <remarks>
///     <para>
///         The recurring form of <see cref="ISchedule" />: it extends the shared schedule contract, so an existing
///         implementation stays assignable to <see cref="RecurringCommandHostOptions.Schedule" />. A new schedule
///         implements <see cref="ISchedule" /> directly, uses the built-in schedules on
///         <see cref="RecurringSchedules" />, or uses the cron schedules package.
///     </para>
///     <para>
///         Implementations must be thread-safe and free of side effects, because the loop calls them on every wait.
///     </para>
/// </remarks>
public interface IRecurringSchedule : ISchedule
{
    /// <summary>
    ///     Gets the next occurrence strictly after the supplied instant.
    /// </summary>
    /// <param name="after">The instant the occurrence must follow.</param>
    /// <returns>
    ///     The next occurrence, or <see langword="null" /> when the schedule has no further occurrence, which ends the
    ///     loop.
    /// </returns>
    new DateTimeOffset? GetNextOccurrence(DateTimeOffset after);
}
