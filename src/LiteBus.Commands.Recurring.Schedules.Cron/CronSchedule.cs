using System;
using Cronos;
using LiteBus.Runtime.Abstractions.Scheduling;

namespace LiteBus.Commands.Recurring.Schedules.Cron;

/// <summary>
///     A schedule that fires at the occurrences of a cron expression read in one zone.
/// </summary>
internal sealed class CronSchedule : ISchedule
{
    /// <summary>
    ///     The parsed cron expression occurrences are read from.
    /// </summary>
    private readonly CronExpression _expression;

    /// <summary>
    ///     The zone the expression is read in.
    /// </summary>
    private readonly TimeZoneInfo _zone;

    /// <summary>
    ///     Initializes a new instance of the <see cref="CronSchedule" /> class.
    /// </summary>
    /// <param name="expression">The parsed cron expression occurrences are read from.</param>
    /// <param name="zone">The zone the expression is read in.</param>
    /// <exception cref="ArgumentNullException"><paramref name="expression" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="zone" /> is <see langword="null" />.</exception>
    public CronSchedule(CronExpression expression, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(expression);
        ArgumentNullException.ThrowIfNull(zone);

        _expression = expression;
        _zone = zone;
    }

    /// <inheritdoc />
    public DateTimeOffset? GetNextOccurrence(DateTimeOffset after)
    {
        return _expression.GetNextOccurrence(after, _zone);
    }
}
