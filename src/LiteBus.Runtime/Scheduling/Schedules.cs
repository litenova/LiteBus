using System;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Runtime.Abstractions.Scheduling;

namespace LiteBus.Runtime.Scheduling;

/// <summary>
///     Builds the schedules LiteBus ships for calendar timing.
/// </summary>
/// <remarks>
///     An application with cron expressions implements <see cref="ISchedule" /> over its cron library instead
///     of using these.
/// </remarks>
/// <example>
///     <code><![CDATA[
/// ISchedule schedule = Schedules.DailyAt(new TimeOnly(9, 0));
/// DateTimeOffset? next = schedule.GetNextOccurrence(DateTimeOffset.UtcNow);
/// ]]></code>
/// </example>
public static class Schedules
{
    /// <summary>
    ///     Creates a schedule that fires once per interval after the current time.
    /// </summary>
    /// <param name="interval">The delay between the current time and the next occurrence.</param>
    /// <returns>The schedule.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="interval" /> is zero or negative.</exception>
    public static ISchedule Every(TimeSpan interval)
    {
        return new IntervalSchedule(interval);
    }

    /// <summary>
    ///     Creates a schedule that fires once a day at a fixed local time.
    /// </summary>
    /// <param name="time">The local time each occurrence fires at.</param>
    /// <param name="zone">The zone the local time is read in, or <see langword="null" /> for UTC.</param>
    /// <returns>The schedule.</returns>
    /// <remarks>
    ///     A local time inside a daylight-saving gap resolves forward to the next valid instant, and a local time
    ///     inside a fall-back overlap resolves to the earlier occurrence.
    /// </remarks>
    public static ISchedule DailyAt(TimeOnly time, TimeZoneInfo? zone = null)
    {
        return new DailySchedule(time, zone ?? TimeZoneInfo.Utc);
    }

    /// <summary>
    ///     Creates a schedule that fires once a week on a fixed weekday at a fixed local time.
    /// </summary>
    /// <param name="day">The weekday each occurrence fires on.</param>
    /// <param name="time">The local time each occurrence fires at.</param>
    /// <param name="zone">The zone the local time is read in, or <see langword="null" /> for UTC.</param>
    /// <returns>The schedule.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="day" /> is not a defined weekday.</exception>
    /// <remarks>
    ///     Daylight-saving gaps and overlaps resolve as <see cref="DailyAt(TimeOnly, TimeZoneInfo)" /> describes.
    /// </remarks>
    public static ISchedule WeeklyOn(DayOfWeek day, TimeOnly time, TimeZoneInfo? zone = null)
    {
        return new WeeklySchedule(day, time, zone ?? TimeZoneInfo.Utc);
    }

    /// <summary>
    ///     Creates a schedule that fires once a day at a fixed local time read in each tenant's zone.
    /// </summary>
    /// <param name="time">The local time each occurrence fires at.</param>
    /// <param name="zoneOf">Resolves the zone one tenant's local time is read in.</param>
    /// <returns>The schedule.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="zoneOf" /> is <see langword="null" />.</exception>
    /// <remarks>
    ///     Daylight-saving gaps and overlaps resolve as <see cref="DailyAt(TimeOnly, TimeZoneInfo)" /> describes. A
    ///     tenant whose zone resolves to <see langword="null" /> reads the local time as UTC.
    /// </remarks>
    public static ITenantSchedule DailyAt(
        TimeOnly time,
        Func<string, CancellationToken, ValueTask<TimeZoneInfo?>> zoneOf)
    {
        return new TenantDailySchedule(time, zoneOf);
    }

    /// <summary>
    ///     Creates a schedule that fires once a week on a fixed weekday at a fixed local time read in each
    ///     tenant's zone.
    /// </summary>
    /// <param name="day">The weekday each occurrence fires on.</param>
    /// <param name="time">The local time each occurrence fires at.</param>
    /// <param name="zoneOf">Resolves the zone one tenant's local time is read in.</param>
    /// <returns>The schedule.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="day" /> is not a defined weekday.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="zoneOf" /> is <see langword="null" />.</exception>
    /// <remarks>
    ///     Daylight-saving gaps and overlaps resolve as <see cref="DailyAt(TimeOnly, TimeZoneInfo)" /> describes. A
    ///     tenant whose zone resolves to <see langword="null" /> reads the local time as UTC.
    /// </remarks>
    public static ITenantSchedule WeeklyOn(
        DayOfWeek day,
        TimeOnly time,
        Func<string, CancellationToken, ValueTask<TimeZoneInfo?>> zoneOf)
    {
        return new TenantWeeklySchedule(day, time, zoneOf);
    }
}
