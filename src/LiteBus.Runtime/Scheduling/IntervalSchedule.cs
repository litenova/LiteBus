using System;
using LiteBus.Runtime.Abstractions.Scheduling;

namespace LiteBus.Runtime.Scheduling;

/// <summary>
///     A schedule that fires once per interval after the current time.
/// </summary>
internal sealed class IntervalSchedule : ISchedule
{
    /// <summary>
    ///     The delay between the current time and the next occurrence.
    /// </summary>
    private readonly TimeSpan _interval;

    /// <summary>
    ///     Initializes a new instance of the <see cref="IntervalSchedule" /> class.
    /// </summary>
    /// <param name="interval">The delay between the current time and the next occurrence.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="interval" /> is zero or negative.</exception>
    public IntervalSchedule(TimeSpan interval)
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
