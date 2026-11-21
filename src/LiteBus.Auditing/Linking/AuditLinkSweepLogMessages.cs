using System;
using Microsoft.Extensions.Logging;

namespace LiteBus.Auditing.Linking;

/// <summary>
///     High-performance audit link sweep log messages.
/// </summary>
internal static class AuditLinkSweepLogMessages
{
    /// <summary>
    ///     The delegate that writes the link sweep failure event.
    /// </summary>
    private static readonly Action<ILogger, TimeSpan, Exception?> SweepFailedMessage =
        LoggerMessage.Define<TimeSpan>(
            LogLevel.Error,
            new EventId(2401, nameof(SweepFailed)),
            "Audit link sweep failed; waiting {Interval} before retry.");

    /// <summary>
    ///     Logs that the link sweep failed and will retry after the sweep interval.
    /// </summary>
    /// <param name="logger">The logger receiving the event.</param>
    /// <param name="exception">The exception that aborted the sweep.</param>
    /// <param name="interval">The delay before the next sweep pass.</param>
    public static void SweepFailed(ILogger logger, Exception exception, TimeSpan interval)
    {
        SweepFailedMessage(logger, interval, exception);
    }
}
