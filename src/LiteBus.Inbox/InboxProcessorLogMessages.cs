using System;
using Microsoft.Extensions.Logging;

namespace LiteBus.Inbox;

/// <summary>
///     High-performance inbox processor log messages.
/// </summary>
internal static class InboxProcessorLogMessages
{
    /// <summary>
    ///     The delegate that writes the inbox processor loop failure event.
    /// </summary>
    private static readonly Action<ILogger, Exception> LoopFailedMessage =
        LoggerMessage.Define(
            LogLevel.Error,
            new EventId(1001, nameof(LoopFailed)),
            "Inbox processor loop failed before the next pass.");

    /// <summary>
    ///     Logs that the inbox processor loop failed before the next pass.
    /// </summary>
    /// <param name="logger">The logger receiving the event.</param>
    /// <param name="exception">The exception that aborted the pass.</param>
    public static void LoopFailed(ILogger logger, Exception exception)
    {
        LoopFailedMessage(logger, exception);
    }
}