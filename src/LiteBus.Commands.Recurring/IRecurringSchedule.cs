using System;

namespace LiteBus.Commands.Recurring;

/// <summary>
///     Decides when the passes of one recurring loop run, as wall-clock occurrences.
/// </summary>
/// <remarks>
///     <para>
///         The replaceable port behind <see cref="RecurringCommandHostOptions.Schedule" />: the loop asks for the next
///         occurrence after the current time before the first pass and between passes. The built-in schedules live on
///         <see cref="RecurringSchedules" />; an application with cron expressions implements this interface over its
///         cron library.
///     </para>
///     <para>
///         Implementations must be thread-safe and free of side effects, because the loop calls them on every wait.
///     </para>
/// </remarks>
public interface IRecurringSchedule
{
    /// <summary>
    ///     Gets the next occurrence strictly after the supplied instant.
    /// </summary>
    /// <param name="after">The instant the occurrence must follow.</param>
    /// <returns>
    ///     The next occurrence, or <see langword="null" /> when the schedule has no further occurrence, which ends the
    ///     loop.
    /// </returns>
    DateTimeOffset? GetNextOccurrence(DateTimeOffset after);
}
