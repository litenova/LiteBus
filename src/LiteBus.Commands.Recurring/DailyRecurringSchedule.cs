using System;

namespace LiteBus.Commands.Recurring;

/// <summary>
///     A recurring schedule that fires once a day at a fixed local time.
/// </summary>
internal sealed class DailyRecurringSchedule : IRecurringSchedule
{
    /// <summary>
    ///     The local time each occurrence fires at.
    /// </summary>
    private readonly TimeOnly _time;

    /// <summary>
    ///     The zone the local time is read in.
    /// </summary>
    private readonly TimeZoneInfo _zone;

    /// <summary>
    ///     Initializes a new instance of the <see cref="DailyRecurringSchedule" /> class.
    /// </summary>
    /// <param name="time">The local time each occurrence fires at.</param>
    /// <param name="zone">The zone the local time is read in.</param>
    /// <exception cref="ArgumentNullException"><paramref name="zone" /> is <see langword="null" />.</exception>
    public DailyRecurringSchedule(TimeOnly time, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);

        _time = time;
        _zone = zone;
    }

    /// <inheritdoc />
    public DateTimeOffset? GetNextOccurrence(DateTimeOffset after)
    {
        var zonedDate = RecurringScheduleDates.ToZonedDate(after, _zone);

        // Resolved instants move forward with the calendar date, so the first date whose instant follows the
        // supplied one is the next occurrence.
        for (var dayOffset = 0; ; dayOffset++)
        {
            var candidate = RecurringScheduleDates.ResolveInstant(_zone, zonedDate.AddDays(dayOffset), _time);

            if (candidate > after)
            {
                return candidate;
            }
        }
    }
}
