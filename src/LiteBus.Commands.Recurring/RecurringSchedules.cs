using System;

namespace LiteBus.Commands.Recurring;

/// <summary>
///     Builds the recurring schedules LiteBus ships for <see cref="RecurringCommandHostOptions.Schedule" />.
/// </summary>
/// <remarks>
///     An application with cron expressions implements <see cref="IRecurringSchedule" /> over its cron library instead
///     of using these.
/// </remarks>
/// <example>
///     <code><![CDATA[
/// registry.AddCommands(commands => commands
///     .Register<SendDueRemindersCommandHandler>()
///     .AddRecurring(
///         () => new SendDueRemindersCommand(),
///         host => host.Schedule = RecurringSchedules.DailyAt(new TimeOnly(9, 0))));
/// ]]></code>
/// </example>
public static class RecurringSchedules
{
    /// <summary>
    ///     Creates a schedule that fires once per interval after the current time.
    /// </summary>
    /// <param name="interval">The delay between the current time and the next occurrence.</param>
    /// <returns>The schedule.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="interval" /> is zero or negative.</exception>
    public static IRecurringSchedule Every(TimeSpan interval)
    {
        return new IntervalRecurringSchedule(interval);
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
    public static IRecurringSchedule DailyAt(TimeOnly time, TimeZoneInfo? zone = null)
    {
        return new DailyRecurringSchedule(time, zone ?? TimeZoneInfo.Utc);
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
    public static IRecurringSchedule WeeklyOn(DayOfWeek day, TimeOnly time, TimeZoneInfo? zone = null)
    {
        return new WeeklyRecurringSchedule(day, time, zone ?? TimeZoneInfo.Utc);
    }
}
