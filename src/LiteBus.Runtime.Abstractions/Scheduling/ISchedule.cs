using System;

namespace LiteBus.Runtime.Abstractions.Scheduling;

/// <summary>
///     Decides when scheduled work runs, as wall-clock occurrences.
/// </summary>
/// <remarks>
///     <para>
///         The replaceable port behind calendar timing: the caller asks for the next occurrence after the current
///         time before each wait. An application with cron expressions implements this interface over its cron
///         library.
///     </para>
///     <para>
///         Implementations must be thread-safe and free of side effects, because callers invoke them on every wait.
///     </para>
/// </remarks>
public interface ISchedule
{
    /// <summary>
    ///     Gets the next occurrence strictly after the supplied instant.
    /// </summary>
    /// <param name="after">The instant the occurrence must follow.</param>
    /// <returns>
    ///     The next occurrence, or <see langword="null" /> when the schedule has no further occurrence.
    /// </returns>
    DateTimeOffset? GetNextOccurrence(DateTimeOffset after);
}
