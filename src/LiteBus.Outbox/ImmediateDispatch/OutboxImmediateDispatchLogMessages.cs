using System;
using Microsoft.Extensions.Logging;

namespace LiteBus.Outbox.ImmediateDispatch;

/// <summary>
///     High-performance immediate dispatch log messages.
/// </summary>
internal static class OutboxImmediateDispatchLogMessages
{
    /// <summary>
    ///     The delegate that writes an immediate dispatch failure event.
    /// </summary>
    private static readonly Action<ILogger, Guid, string, string, Exception?> ImmediateDispatchFailedMessage =
        LoggerMessage.Define<Guid, string, string>(
            LogLevel.Warning,
            new EventId(2010, nameof(ImmediateDispatchFailed)),
            "Immediate dispatch failed for envelope {MessageId} with contract {ContractName} for handler {Handler}. The envelope was persisted with the processor outcome.");

    /// <summary>
    ///     The delegate that writes an immediate dispatch timeout event.
    /// </summary>
    private static readonly Action<ILogger, Guid, string, string, Exception?> ImmediateDispatchTimedOutMessage =
        LoggerMessage.Define<Guid, string, string>(
            LogLevel.Warning,
            new EventId(2011, nameof(ImmediateDispatchTimedOut)),
            "Immediate dispatch timed out for envelope {MessageId} with contract {ContractName} for handler {Handler}. The envelope was released for the outbox processor.");

    /// <summary>
    ///     The delegate that writes a background immediate dispatch failure event.
    /// </summary>
    private static readonly Action<ILogger, Exception?> BackgroundDispatchFailedMessage =
        LoggerMessage.Define(
            LogLevel.Warning,
            new EventId(2012, nameof(BackgroundDispatchFailed)),
            "Immediate dispatch in background mode failed. The staged envelopes remain for the outbox processor.");

    /// <summary>
    ///     Logs that immediate dispatch failed one envelope.
    /// </summary>
    /// <param name="logger">The logger receiving the event.</param>
    /// <param name="messageId">The envelope that failed dispatch.</param>
    /// <param name="contractName">The stable contract name of the envelope.</param>
    /// <param name="handler">The handler the envelope targets, or the broadcast marker when it targets every handler.</param>
    public static void ImmediateDispatchFailed(ILogger logger, Guid messageId, string contractName, string handler)
    {
        ImmediateDispatchFailedMessage(logger, messageId, contractName, handler, null);
    }

    /// <summary>
    ///     Logs that immediate dispatch released one envelope because the timeout expired.
    /// </summary>
    /// <param name="logger">The logger receiving the event.</param>
    /// <param name="messageId">The envelope that was released.</param>
    /// <param name="contractName">The stable contract name of the envelope.</param>
    /// <param name="handler">The handler the envelope targets, or the broadcast marker when it targets every handler.</param>
    public static void ImmediateDispatchTimedOut(ILogger logger, Guid messageId, string contractName, string handler)
    {
        ImmediateDispatchTimedOutMessage(logger, messageId, contractName, handler, null);
    }

    /// <summary>
    ///     Logs that background immediate dispatch failed before it finished.
    /// </summary>
    /// <param name="logger">The logger receiving the event.</param>
    /// <param name="exception">The exception that ended the background dispatch.</param>
    public static void BackgroundDispatchFailed(ILogger logger, Exception exception)
    {
        BackgroundDispatchFailedMessage(logger, exception);
    }
}
