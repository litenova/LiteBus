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

    /// <summary>
    ///     The delegate that writes the reaction retry notice.
    /// </summary>
    private static readonly Action<ILogger, Guid, int, double, Exception?> ReactionRetryScheduledMessage =
        LoggerMessage.Define<Guid, int, double>(
            LogLevel.Debug,
            new EventId(2202, nameof(ReactionRetryScheduled)),
            "Outbox reaction for envelope {EnvelopeId} met a storage conflict on attempt {ReactionAttempt}; " +
            "publishing again after {DelayMs} ms.");

    /// <summary>
    ///     Logs that a conflicted reaction publishes again inside the same dispatch.
    /// </summary>
    /// <param name="logger">The logger receiving the event.</param>
    /// <param name="envelopeId">The identifier of the envelope being dispatched.</param>
    /// <param name="reactionAttempt">The 1-based in-dispatch attempt that just failed.</param>
    /// <param name="delay">The delay before the next in-dispatch attempt.</param>
    public static void ReactionRetryScheduled(ILogger logger, Guid envelopeId, int reactionAttempt, TimeSpan delay)
    {
        ReactionRetryScheduledMessage(logger, envelopeId, reactionAttempt, delay.TotalMilliseconds, null);
    }

    /// <summary>
    ///     The delegate that writes the reaction retries exhausted notice.
    /// </summary>
    private static readonly Action<ILogger, Guid, int, Exception?> ReactionRetriesExhaustedMessage =
        LoggerMessage.Define<Guid, int>(
            LogLevel.Information,
            new EventId(2203, nameof(ReactionRetriesExhausted)),
            "Outbox reaction for envelope {EnvelopeId} met a storage conflict on attempt {ReactionAttempt} with no " +
            "further in-dispatch retry; the envelope follows the ordinary retry schedule.");

    /// <summary>
    ///     Logs that a dispatch met a storage conflict it does not retry in dispatch.
    /// </summary>
    /// <param name="logger">The logger receiving the event.</param>
    /// <param name="envelopeId">The identifier of the envelope being dispatched.</param>
    /// <param name="reactionAttempt">The 1-based in-dispatch attempt that just failed.</param>
    public static void ReactionRetriesExhausted(ILogger logger, Guid envelopeId, int reactionAttempt)
    {
        ReactionRetriesExhaustedMessage(logger, envelopeId, reactionAttempt, null);
    }

    /// <summary>
    ///     The delegate that writes the reaction retry policy failure notice.
    /// </summary>
    private static readonly Action<ILogger, Guid, Exception?> ReactionRetryPolicyFailedMessage =
        LoggerMessage.Define<Guid>(
            LogLevel.Warning,
            new EventId(2204, nameof(ReactionRetryPolicyFailed)),
            "Outbox reaction retry policy threw for envelope {EnvelopeId}; the envelope follows the ordinary retry schedule.");

    /// <summary>
    ///     Logs that the reaction retry policy threw, so the conflicted envelope follows the ordinary schedule.
    /// </summary>
    /// <param name="logger">The logger receiving the event.</param>
    /// <param name="envelopeId">The identifier of the envelope being dispatched.</param>
    /// <param name="exception">The exception the policy threw.</param>
    public static void ReactionRetryPolicyFailed(ILogger logger, Guid envelopeId, Exception exception)
    {
        ReactionRetryPolicyFailedMessage(logger, envelopeId, exception);
    }
}
