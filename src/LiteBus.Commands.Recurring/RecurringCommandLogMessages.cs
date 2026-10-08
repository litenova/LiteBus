using System;
using Microsoft.Extensions.Logging;

namespace LiteBus.Commands.Recurring;

/// <summary>
///     High-performance recurring command loop log messages.
/// </summary>
internal static class RecurringCommandLogMessages
{
    /// <summary>
    ///     The delegate that writes the loop start event.
    /// </summary>
    private static readonly Action<ILogger, string, TimeSpan, Exception?> LoopStartedMessage =
        LoggerMessage.Define<string, TimeSpan>(
            LogLevel.Information,
            new EventId(2101, nameof(LoopStarted)),
            "Recurring command loop for {CommandType} started with interval {Interval}.");

    /// <summary>
    ///     The delegate that writes the disabled-loop event.
    /// </summary>
    private static readonly Action<ILogger, string, Exception?> LoopDisabledMessage =
        LoggerMessage.Define<string>(
            LogLevel.Information,
            new EventId(2102, nameof(LoopDisabled)),
            "Recurring command loop for {CommandType} is disabled and will not run.");

    /// <summary>
    ///     The delegate that writes a failed-pass event.
    /// </summary>
    private static readonly Action<ILogger, string, Exception> PassFailedMessage =
        LoggerMessage.Define<string>(
            LogLevel.Error,
            new EventId(2103, nameof(PassFailed)),
            "Recurring command {CommandType} failed. The loop continues after the configured error backoff.");

    /// <summary>
    ///     The delegate that writes a concurrency-conflict event.
    /// </summary>
    private static readonly Action<ILogger, string, string, Exception?> PassConflictedMessage =
        LoggerMessage.Define<string, string>(
            LogLevel.Information,
            new EventId(2104, nameof(PassConflicted)),
            "Recurring command {CommandType} lost a concurrency race ({ConflictType}); another instance changed the same data. The loop continues at its normal interval.");

    /// <summary>
    ///     Writes the loop start event.
    /// </summary>
    /// <param name="logger">The logger receiving the event.</param>
    /// <param name="commandType">The command type sent by the loop.</param>
    /// <param name="interval">The delay between passes.</param>
    internal static void LoopStarted(ILogger logger, string commandType, TimeSpan interval)
    {
        LoopStartedMessage(logger, commandType, interval, null);
    }

    /// <summary>
    ///     Writes the disabled-loop event.
    /// </summary>
    /// <param name="logger">The logger receiving the event.</param>
    /// <param name="commandType">The command type the disabled loop would have sent.</param>
    internal static void LoopDisabled(ILogger logger, string commandType)
    {
        LoopDisabledMessage(logger, commandType, null);
    }

    /// <summary>
    ///     Writes a failed-pass event.
    /// </summary>
    /// <param name="logger">The logger receiving the event.</param>
    /// <param name="commandType">The command type sent by the loop.</param>
    /// <param name="exception">The exception the pass threw.</param>
    internal static void PassFailed(ILogger logger, string commandType, Exception exception)
    {
        PassFailedMessage(logger, commandType, exception);
    }

    /// <summary>
    ///     Writes a concurrency-conflict event.
    /// </summary>
    /// <param name="logger">The logger receiving the event.</param>
    /// <param name="commandType">The command type sent by the loop.</param>
    /// <param name="conflictType">The type name of the exception classified as a conflict.</param>
    internal static void PassConflicted(ILogger logger, string commandType, string conflictType)
    {
        PassConflictedMessage(logger, commandType, conflictType, null);
    }
}
