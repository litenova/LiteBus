using System;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Runtime.Abstractions.Scheduling;

namespace LiteBus.Runtime.Scheduling;

/// <summary>
///     A schedule that fires once a week on a fixed weekday at a fixed local time read in each tenant's zone.
/// </summary>
internal sealed class TenantWeeklySchedule : ITenantSchedule
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
    ///     Resolves the zone one tenant's local time is read in.
    /// </summary>
    private readonly Func<string, CancellationToken, ValueTask<TimeZoneInfo?>> _zoneOf;

    /// <summary>
    ///     Initializes a new instance of the <see cref="TenantWeeklySchedule" /> class.
    /// </summary>
    /// <param name="day">The weekday each occurrence fires on.</param>
    /// <param name="time">The local time each occurrence fires at.</param>
    /// <param name="zoneOf">Resolves the zone one tenant's local time is read in.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="day" /> is not a defined weekday.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="zoneOf" /> is <see langword="null" />.</exception>
    public TenantWeeklySchedule(
        DayOfWeek day,
        TimeOnly time,
        Func<string, CancellationToken, ValueTask<TimeZoneInfo?>> zoneOf)
    {
        if (!Enum.IsDefined(day))
        {
            throw new ArgumentOutOfRangeException(nameof(day), day, "The weekday must be a defined DayOfWeek value.");
        }

        ArgumentNullException.ThrowIfNull(zoneOf);

        _day = day;
        _time = time;
        _zoneOf = zoneOf;
    }

    /// <inheritdoc />
    public async ValueTask<DateTimeOffset?> GetNextOccurrenceAsync(
        string tenantId,
        DateTimeOffset after,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        var resolved = await _zoneOf(tenantId, cancellationToken).ConfigureAwait(false);
        var zone = resolved ?? TimeZoneInfo.Utc;
        var zonedDate = ScheduleDates.ToZonedDate(after, zone);

        // The weekday recurs every seven days with increasing instants, so the first matching date whose instant
        // follows the supplied one is the next occurrence.
        for (var dayOffset = 0; ; dayOffset++)
        {
            var date = zonedDate.AddDays(dayOffset);

            if (date.DayOfWeek != _day)
            {
                continue;
            }

            var candidate = ScheduleDates.ResolveInstant(zone, date, _time);

            if (candidate > after)
            {
                return candidate;
            }
        }
    }
}
