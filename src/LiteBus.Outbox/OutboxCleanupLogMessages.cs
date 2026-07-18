using System;
using Microsoft.Extensions.Logging;

namespace LiteBus.Outbox;

/// <summary>
///     High-performance outbox retention cleanup log messages.
/// </summary>
internal static class OutboxCleanupLogMessages
{
    /// <summary>
    ///     The delegate that writes the outbox retention cleanup failure event.
    /// </summary>
    private static readonly Action<ILogger, TimeSpan, Exception?> CleanupFailedMessage =
        LoggerMessage.Define<TimeSpan>(
            LogLevel.Error,
            new EventId(2101, nameof(CleanupFailed)),
            "Outbox retention cleanup failed; waiting {Backoff} before retry.");

    /// <summary>
    ///     Logs that outbox retention cleanup failed and will retry after a backoff delay.
    /// </summary>
    /// <param name="logger">The logger receiving the event.</param>
    /// <param name="exception">The exception that aborted cleanup.</param>
    /// <param name="backoff">The delay before the next retry attempt.</param>
    public static void CleanupFailed(ILogger logger, Exception exception, TimeSpan backoff)
    {
        CleanupFailedMessage(logger, backoff, exception);
    }
}