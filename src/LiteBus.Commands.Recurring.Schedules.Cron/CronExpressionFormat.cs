namespace LiteBus.Commands.Recurring.Schedules.Cron;

/// <summary>
///     Chooses the fields a cron expression passed to <see cref="CronSchedules" /> contains.
/// </summary>
public enum CronExpressionFormat
{
    /// <summary>
    ///     Five fields: minute, hour, day of month, month, day of week.
    /// </summary>
    Standard = 0,

    /// <summary>
    ///     Six fields: second, minute, hour, day of month, month, day of week.
    /// </summary>
    IncludeSeconds = 1
}
