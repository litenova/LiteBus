using System;
using Microsoft.Extensions.Logging;

namespace LiteBus.Inbox;

/// <summary>
///     High-performance inbox retention cleanup log messages.
/// </summary>
internal static class InboxCleanupLogMessages
{
    /// <summary>
    ///     The delegate that writes the inbox retention cleanup failure event.
    /// </summary>
    private static readonly Action<ILogger, TimeSpan, Exception?> CleanupFailedMessage =
        LoggerMessage.Define<TimeSpan>(
            LogLevel.Error,
            new EventId(1101, nameof(CleanupFailed)),
            "Inbox retention cleanup failed; waiting {Backoff} before retry.");

    /// <summary>
    ///     Logs that inbox retention cleanup failed and will retry after a backoff delay.
    /// </summary>
    /// <param name="logger">The logger receiving the event.</param>
    /// <param name="exception">The exception that aborted cleanup.</param>
    /// <param name="backoff">The delay before the next retry attempt.</param>
    public static void CleanupFailed(ILogger logger, Exception exception, TimeSpan backoff)
    {
        CleanupFailedMessage(logger, backoff, exception);
    }
}