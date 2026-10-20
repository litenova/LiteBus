using System;
using Microsoft.Extensions.Logging;

namespace LiteBus.Runtime.Extensions.Hosting;

/// <summary>
///     High-performance background service host wrapper log messages.
/// </summary>
internal static class BackgroundServiceHostWrapperLogMessages
{
    /// <summary>
    ///     The delegate that writes the abandoned background loop event.
    /// </summary>
    private static readonly Action<ILogger, string, Exception?> LoopAbandonedMessage =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(6001, nameof(LoopAbandoned)),
            "Background service loop abandoned after the host stop budget expired. ServiceType={ServiceType}");

    /// <summary>
    ///     Logs that a background service loop was abandoned because it ignored host shutdown.
    /// </summary>
    /// <param name="logger">The logger receiving the event.</param>
    /// <param name="serviceType">The full name of the background service type whose loop was abandoned.</param>
    public static void LoopAbandoned(ILogger logger, string serviceType)
    {
        LoopAbandonedMessage(logger, serviceType, null);
    }
}
