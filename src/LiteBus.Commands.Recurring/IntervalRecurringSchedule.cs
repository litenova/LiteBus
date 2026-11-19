using System;

namespace LiteBus.Commands.Recurring;

/// <summary>
///     A recurring schedule that fires once per interval after the current time.
/// </summary>
internal sealed class IntervalRecurringSchedule : IRecurringSchedule
{
    /// <summary>
    ///     The delay between the current time and the next occurrence.
    /// </summary>
    private readonly TimeSpan _interval;

    /// <summary>
    ///     Initializes a new instance of the <see cref="IntervalRecurringSchedule" /> class.
    /// </summary>
    /// <param name="interval">The delay between the current time and the next occurrence.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="interval" /> is zero or negative.</exception>
    public IntervalRecurringSchedule(TimeSpan interval)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(interval.Ticks);

        _interval = interval;
    }

    /// <inheritdoc />
    public DateTimeOffset? GetNextOccurrence(DateTimeOffset after)
    {
        return after.Add(_interval);
    }
}
