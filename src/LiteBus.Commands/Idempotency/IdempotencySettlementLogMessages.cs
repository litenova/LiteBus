using System;
using Microsoft.Extensions.Logging;

namespace LiteBus.Commands.Idempotency;

/// <summary>
///     High-performance idempotency settlement log messages.
/// </summary>
internal static class IdempotencySettlementLogMessages
{
    /// <summary>
    ///     The delegate that writes a post-commit completion failure event.
    /// </summary>
    private static readonly Action<ILogger, string, Exception> PostCommitCompleteFailedMessage =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(2201, nameof(PostCommitCompleteFailed)),
            "Idempotency key {Key} could not be marked complete after the unit of work committed. The data is committed; the key stays claimed until the store's claim timeout, after which a repeat runs again.");

    /// <summary>
    ///     The delegate that writes a release failure event after a failed commit.
    /// </summary>
    private static readonly Action<ILogger, string, Exception> PostCommitReleaseFailedMessage =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(2202, nameof(PostCommitReleaseFailed)),
            "Idempotency key {Key} could not be released after the unit of work commit failed. The commit exception is preserved; the key stays claimed until the store's claim timeout, after which a repeat runs again.");

    /// <summary>
    ///     Writes a post-commit completion failure event.
    /// </summary>
    /// <param name="logger">The logger receiving the event.</param>
    /// <param name="key">The idempotency key that could not be marked complete.</param>
    /// <param name="exception">The exception the final completion attempt threw.</param>
    internal static void PostCommitCompleteFailed(ILogger logger, string key, Exception exception)
    {
        PostCommitCompleteFailedMessage(logger, key, exception);
    }

    /// <summary>
    ///     Writes a release failure event after a failed commit.
    /// </summary>
    /// <param name="logger">The logger receiving the event.</param>
    /// <param name="key">The idempotency key that could not be released.</param>
    /// <param name="exception">The exception the release attempt threw.</param>
    internal static void PostCommitReleaseFailed(ILogger logger, string key, Exception exception)
    {
        PostCommitReleaseFailedMessage(logger, key, exception);
    }
}
