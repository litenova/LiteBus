using System;
using Microsoft.Extensions.Logging;

namespace LiteBus.Auditing.Verification;

/// <summary>
///     High-performance audit verification log messages.
/// </summary>
internal static class AuditVerifierLogMessages
{
    /// <summary>
    ///     The delegate that writes the unreachable checkpoint source event.
    /// </summary>
    private static readonly Action<ILogger, string, string, Exception?> SourceUnreachableMessage =
        LoggerMessage.Define<string, string>(
            LogLevel.Warning,
            new EventId(2501, nameof(SourceUnreachable)),
            "The checkpoint source {SourceName} could not be reached while verifying segment '{Segment}'. The verification stays intact and the comparison records the gap.");

    /// <summary>
    ///     Logs that a checkpoint source could not be reached during verification.
    /// </summary>
    /// <param name="logger">The logger receiving the event.</param>
    /// <param name="sourceName">The name of the source that could not be reached.</param>
    /// <param name="segment">The segment under verification.</param>
    /// <param name="exception">The exception the source read threw.</param>
    public static void SourceUnreachable(ILogger logger, string sourceName, string segment, Exception exception)
    {
        SourceUnreachableMessage(logger, sourceName, segment, exception);
    }
}
