using System;
using System.Threading;
using System.Threading.Tasks;
using Cronos;
using LiteBus.Runtime.Abstractions.Scheduling;

namespace LiteBus.Commands.Recurring.Schedules.Cron;

/// <summary>
///     Builds cron schedules backed by Cronos for calendar timing.
/// </summary>
/// <remarks>
///     Parsing stays in Cronos; this package only adapts its occurrences to the schedule ports, so applications that
///     do not reference this package take no new dependency.
/// </remarks>
/// <example>
///     <code><![CDATA[
/// ISchedule schedule = CronSchedules.FromExpression("0 9 * * *");
/// DateTimeOffset? next = schedule.GetNextOccurrence(DateTimeOffset.UtcNow);
/// ]]></code>
/// </example>
public static class CronSchedules
{
    /// <summary>
    ///     Creates a schedule that fires at the occurrences of a five-field cron expression read in one zone.
    /// </summary>
    /// <param name="expression">The cron expression, with minute, hour, day of month, month, and day of week fields.</param>
    /// <param name="zone">The zone the expression is read in, or <see langword="null" /> for UTC.</param>
    /// <returns>The schedule.</returns>
    /// <exception cref="ArgumentException"><paramref name="expression" /> is <see langword="null" />, empty, or whitespace.</exception>
    /// <exception cref="FormatException"><paramref name="expression" /> is not a valid cron expression.</exception>
    /// <remarks>
    ///     A cron occurrence inside a daylight-saving gap resolves forward to the next valid instant, and an occurrence
    ///     inside a fall-back overlap resolves to the earlier occurrence. An expression whose second, minute, or hour
    ///     field repeats on an interval fires on both sides of the repeated hour instead of once.
    /// </remarks>
    public static ISchedule FromExpression(string expression, TimeZoneInfo? zone = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expression);

        return new CronSchedule(ParseExpression(expression, CronExpressionFormat.Standard), zone ?? TimeZoneInfo.Utc);
    }

    /// <summary>
    ///     Creates a schedule that fires at the occurrences of a cron expression read in one zone.
    /// </summary>
    /// <param name="expression">The cron expression to read.</param>
    /// <param name="format">The fields the expression contains.</param>
    /// <param name="zone">The zone the expression is read in, or <see langword="null" /> for UTC.</param>
    /// <returns>The schedule.</returns>
    /// <exception cref="ArgumentException"><paramref name="expression" /> is <see langword="null" />, empty, or whitespace.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="format" /> is not a defined format.</exception>
    /// <exception cref="FormatException"><paramref name="expression" /> is not a valid cron expression.</exception>
    /// <remarks>
    ///     Daylight-saving gaps and overlaps resolve as <see cref="FromExpression(string, TimeZoneInfo)" /> describes.
    /// </remarks>
    public static ISchedule FromExpression(string expression, CronExpressionFormat format, TimeZoneInfo? zone = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expression);

        return new CronSchedule(ParseExpression(expression, format), zone ?? TimeZoneInfo.Utc);
    }

    /// <summary>
    ///     Creates a schedule that fires at the occurrences of a five-field cron expression read in each tenant's
    ///     zone.
    /// </summary>
    /// <param name="expression">The cron expression, with minute, hour, day of month, month, and day of week fields.</param>
    /// <param name="zoneOf">Resolves the zone one tenant's expression is read in.</param>
    /// <returns>The schedule.</returns>
    /// <exception cref="ArgumentException"><paramref name="expression" /> is <see langword="null" />, empty, or whitespace.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="zoneOf" /> is <see langword="null" />.</exception>
    /// <exception cref="FormatException"><paramref name="expression" /> is not a valid cron expression.</exception>
    /// <remarks>
    ///     Daylight-saving gaps and overlaps resolve as <see cref="FromExpression(string, TimeZoneInfo)" /> describes. A
    ///     tenant whose zone resolves to <see langword="null" /> reads the expression as UTC.
    /// </remarks>
    public static ITenantSchedule FromExpression(
        string expression,
        Func<string, CancellationToken, ValueTask<TimeZoneInfo?>> zoneOf)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expression);
        ArgumentNullException.ThrowIfNull(zoneOf);

        return new TenantCronSchedule(ParseExpression(expression, CronExpressionFormat.Standard), zoneOf);
    }

    /// <summary>
    ///     Creates a schedule that fires at the occurrences of a cron expression read in each tenant's zone.
    /// </summary>
    /// <param name="expression">The cron expression to read.</param>
    /// <param name="format">The fields the expression contains.</param>
    /// <param name="zoneOf">Resolves the zone one tenant's expression is read in.</param>
    /// <returns>The schedule.</returns>
    /// <exception cref="ArgumentException"><paramref name="expression" /> is <see langword="null" />, empty, or whitespace.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="format" /> is not a defined format.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="zoneOf" /> is <see langword="null" />.</exception>
    /// <exception cref="FormatException"><paramref name="expression" /> is not a valid cron expression.</exception>
    /// <remarks>
    ///     Daylight-saving gaps and overlaps resolve as <see cref="FromExpression(string, TimeZoneInfo)" /> describes. A
    ///     tenant whose zone resolves to <see langword="null" /> reads the expression as UTC.
    /// </remarks>
    public static ITenantSchedule FromExpression(
        string expression,
        CronExpressionFormat format,
        Func<string, CancellationToken, ValueTask<TimeZoneInfo?>> zoneOf)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expression);
        ArgumentNullException.ThrowIfNull(zoneOf);

        return new TenantCronSchedule(ParseExpression(expression, format), zoneOf);
    }

    /// <summary>
    ///     Parses a cron expression in the supplied format.
    /// </summary>
    /// <param name="expression">The cron expression to parse.</param>
    /// <param name="format">The fields the expression contains.</param>
    /// <returns>The parsed expression.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="format" /> is not a defined format.</exception>
    /// <exception cref="FormatException"><paramref name="expression" /> is not a valid cron expression.</exception>
    private static CronExpression ParseExpression(string expression, CronExpressionFormat format)
    {
        if (!Enum.IsDefined(format))
        {
            throw new ArgumentOutOfRangeException(nameof(format), format, "The format must be a defined CronExpressionFormat value.");
        }

        var cronFormat = format == CronExpressionFormat.IncludeSeconds
            ? CronFormat.IncludeSeconds
            : CronFormat.Standard;

        return CronExpression.Parse(expression, cronFormat);
    }
}
