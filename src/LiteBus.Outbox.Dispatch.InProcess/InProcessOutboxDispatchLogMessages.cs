using System;
using Microsoft.Extensions.Logging;

namespace LiteBus.Outbox.Dispatch.InProcess;

/// <summary>
///     High-performance in-process outbox dispatch log messages.
/// </summary>
internal static class InProcessOutboxDispatchLogMessages
{
    /// <summary>
    ///     The delegate that writes the envelopes-without-handler startup warning.
    /// </summary>
    private static readonly Action<ILogger, string, Exception?> EnvelopesWithoutHandlerAllowedMessage =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(2201, nameof(EnvelopesWithoutHandlerAllowed)),
            "In-process outbox dispatch allows envelopes without a handler. Reason: {Reason}. " +
            "Envelopes that reach no handler complete as published.");

    /// <summary>
    ///     Logs that in-process outbox dispatch allows envelopes without a handler.
    /// </summary>
    /// <param name="logger">The logger receiving the event.</param>
    /// <param name="reason">The acknowledgement recorded for allowing envelopes without a handler.</param>
    public static void EnvelopesWithoutHandlerAllowed(ILogger logger, string reason)
    {
        EnvelopesWithoutHandlerAllowedMessage(logger, reason, null);
    }
}
