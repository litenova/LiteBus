using System;
using Microsoft.Extensions.Logging;

namespace LiteBus.Inbox.Ingress;

/// <summary>
///     High-performance log message definitions for transport inbox ingress.
/// </summary>
internal static class TransportInboxIngressLogMessages
{
    /// <summary>
    ///     The delegate that writes the transport ingress restart event.
    /// </summary>
    private static readonly Action<ILogger, Exception> IngressRestartingMessage =
        LoggerMessage.Define(
            LogLevel.Warning,
            new EventId(3002, nameof(IngressRestarting)),
            "Transport inbox ingress loop failed and will restart.");

    /// <summary>
    ///     The delegate that writes the batch flush failure event.
    /// </summary>
    private static readonly Action<ILogger, Exception> BatchFlushFailedMessage =
        LoggerMessage.Define(
            LogLevel.Error,
            new EventId(3003, nameof(BatchFlushFailed)),
            "Transport inbox ingress batch flush failed after BatchMaxWait elapsed.");

    /// <summary>
    ///     The delegate that writes the acknowledgement failure after accept event.
    /// </summary>
    private static readonly Action<ILogger, Exception> AckFailedAfterAcceptMessage =
        LoggerMessage.Define(
            LogLevel.Error,
            new EventId(3004, nameof(AckFailedAfterAccept)),
            "Transport inbox ingress acknowledgement failed after the delivery was accepted.");

    /// <summary>
    ///     Logs that the transport inbox ingress loop will restart after a failure.
    /// </summary>
    /// <param name="logger">The logger receiving the event.</param>
    /// <param name="exception">The exception that stopped the ingress loop.</param>
    public static void IngressRestarting(ILogger logger, Exception exception)
    {
        IngressRestartingMessage(logger, exception);
    }

    /// <summary>
    ///     Logs that a timed batch flush failed after <see cref="TransportInboxIngressSafetyOptions.BatchMaxWait" />
    ///     elapsed.
    /// </summary>
    /// <param name="logger">The logger receiving the event.</param>
    /// <param name="exception">The exception thrown while flushing the partial batch.</param>
    public static void BatchFlushFailed(ILogger logger, Exception exception)
    {
        BatchFlushFailedMessage(logger, exception);
    }

    /// <summary>
    ///     Logs that broker acknowledgement failed after the inbox store accepted the delivery.
    /// </summary>
    /// <param name="logger">The logger receiving the event.</param>
    /// <param name="exception">The exception thrown while acknowledging the broker delivery.</param>
    public static void AckFailedAfterAccept(ILogger logger, Exception exception)
    {
        AckFailedAfterAcceptMessage(logger, exception);
    }
}
