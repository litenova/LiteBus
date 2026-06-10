using System;
using Microsoft.Extensions.Logging;

namespace LiteBus.Outbox;

/// <summary>
///     High-performance outbox processor log messages.
/// </summary>
internal static class OutboxProcessorLogMessages
{
    /// <summary>
    ///     The delegate that writes the outbox processor loop failure event.
    /// </summary>
    private static readonly Action<ILogger, Exception> LoopFailedMessage =
        LoggerMessage.Define(
            LogLevel.Error,
            new EventId(2001, nameof(LoopFailed)),
            "Outbox processor loop failed before the next pass.");

    /// <summary>
    ///     Logs that the outbox processor loop failed before the next pass.
    /// </summary>
    /// <param name="logger">The logger receiving the event.</param>
    /// <param name="exception">The exception that aborted the pass.</param>
    public static void LoopFailed(ILogger logger, Exception exception) =>
        LoopFailedMessage(logger, exception);
}
