using System;

namespace LiteBus.Messaging.Abstractions.Processing;

/// <summary>
///     Lightweight stopwatch used to measure processor pass duration without allocating
///     <see cref="System.Diagnostics.Stopwatch" />.
/// </summary>
public readonly struct ProcessorPassStopwatch
{
    /// <summary>
    ///     The timestamp captured when the stopwatch started.
    /// </summary>
    private readonly long _startedTimestamp;

    /// <summary>
    ///     Starts a new stopwatch instance.
    /// </summary>
    /// <returns>The running stopwatch value.</returns>
    public static ProcessorPassStopwatch StartNew()
    {
        return new ProcessorPassStopwatch(Environment.TickCount64);
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="ProcessorPassStopwatch" /> struct.
    /// </summary>
    /// <param name="startedTimestamp">The tick count captured at start.</param>
    private ProcessorPassStopwatch(long startedTimestamp)
    {
        _startedTimestamp = startedTimestamp;
    }

    /// <summary>
    ///     Gets the elapsed time since the stopwatch was started.
    /// </summary>
    /// <returns>The elapsed duration.</returns>
    public TimeSpan GetElapsedTime()
    {
        var elapsedTicks = Environment.TickCount64 - _startedTimestamp;
        return TimeSpan.FromMilliseconds(elapsedTicks);
    }
}
