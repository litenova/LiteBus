using System;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Runtime.Abstractions.Scheduling;

namespace LiteBus.Runtime.Scheduling;

/// <summary>
///     A schedule that fires once a day at a fixed local time read in each tenant's zone.
/// </summary>
internal sealed class TenantDailySchedule : ITenantSchedule
{
    /// <summary>
    ///     The local time each occurrence fires at.
    /// </summary>
    private readonly TimeOnly _time;

    /// <summary>
    ///     Resolves the zone one tenant's local time is read in.
    /// </summary>
    private readonly Func<string, CancellationToken, ValueTask<TimeZoneInfo?>> _zoneOf;

    /// <summary>
    ///     Initializes a new instance of the <see cref="TenantDailySchedule" /> class.
    /// </summary>
    /// <param name="time">The local time each occurrence fires at.</param>
    /// <param name="zoneOf">Resolves the zone one tenant's local time is read in.</param>
    /// <exception cref="ArgumentNullException"><paramref name="zoneOf" /> is <see langword="null" />.</exception>
    public TenantDailySchedule(TimeOnly time, Func<string, CancellationToken, ValueTask<TimeZoneInfo?>> zoneOf)
    {
        ArgumentNullException.ThrowIfNull(zoneOf);

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

        // Resolved instants move forward with the calendar date, so the first date whose instant follows the
        // supplied one is the next occurrence.
        for (var dayOffset = 0; ; dayOffset++)
        {
            var candidate = ScheduleDates.ResolveInstant(zone, zonedDate.AddDays(dayOffset), _time);

            if (candidate > after)
            {
                return candidate;
            }
        }
    }
}
