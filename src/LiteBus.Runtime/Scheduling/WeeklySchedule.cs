using System;
using LiteBus.Runtime.Abstractions.Scheduling;

namespace LiteBus.Runtime.Scheduling;

/// <summary>
///     A schedule that fires once a week on a fixed weekday at a fixed local time.
/// </summary>
internal sealed class WeeklySchedule : ISchedule
{
    /// <summary>
    ///     The weekday each occurrence fires on.
    /// </summary>
    private readonly DayOfWeek _day;

    /// <summary>
    ///     The local time each occurrence fires at.
    /// </summary>
    private readonly TimeOnly _time;

    /// <summary>
    ///     The zone the local time is read in.
    /// </summary>
    private readonly TimeZoneInfo _zone;

    /// <summary>
    ///     Initializes a new instance of the <see cref="WeeklySchedule" /> class.
    /// </summary>
    /// <param name="day">The weekday each occurrence fires on.</param>
    /// <param name="time">The local time each occurrence fires at.</param>
    /// <param name="zone">The zone the local time is read in.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="day" /> is not a defined weekday.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="zone" /> is <see langword="null" />.</exception>
    public WeeklySchedule(DayOfWeek day, TimeOnly time, TimeZoneInfo zone)
    {
        if (!Enum.IsDefined(day))
        {
            throw new ArgumentOutOfRangeException(nameof(day), day, "The weekday must be a defined DayOfWeek value.");
        }

        ArgumentNullException.ThrowIfNull(zone);

        _day = day;
        _time = time;
        _zone = zone;
    }

    /// <inheritdoc />
    public DateTimeOffset? GetNextOccurrence(DateTimeOffset after)
    {
        var zonedDate = ScheduleDates.ToZonedDate(after, _zone);

        // The weekday recurs every seven days with increasing instants, so the first matching date whose instant
        // follows the supplied one is the next occurrence.
        for (var dayOffset = 0; ; dayOffset++)
        {
            var date = zonedDate.AddDays(dayOffset);

            if (date.DayOfWeek != _day)
            {
                continue;
            }

            var candidate = ScheduleDates.ResolveInstant(_zone, date, _time);

            if (candidate > after)
            {
                return candidate;
            }
        }
    }
}
