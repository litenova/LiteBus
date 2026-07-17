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
    ///     The delegate that writes an outbox dispatch failure event.
    /// </summary>
    private static readonly Action<ILogger, Guid, int, Exception> DispatchFailedMessage =
        LoggerMessage.Define<Guid, int>(
            LogLevel.Warning,
            new EventId(2002, nameof(DispatchFailed)),
            "Outbox dispatch failed for message {MessageId} on attempt {AttemptCount}.");

    /// <summary>
    ///     The delegate that writes a skipped lease-loss persistence event.
    /// </summary>
    private static readonly Action<ILogger, Guid, Exception?> LeaseLossPersistSkippedMessage =
        LoggerMessage.Define<Guid>(
            LogLevel.Warning,
            new EventId(2003, nameof(LeaseLossPersistSkipped)),
            "Outbox lease-loss persistence was skipped for message {MessageId} because the active lease was lost.");

    /// <summary>
    ///     The delegate that writes a tolerated post-dispatch hook failure event.
    /// </summary>
    private static readonly Action<ILogger, Guid, Exception> AfterDispatchCompletedMessage =
        LoggerMessage.Define<Guid>(
            LogLevel.Warning,
            new EventId(2004, nameof(AfterDispatchCompleted)),
            "The outbox AfterDispatch hook failed for message {MessageId}; completing dispatch despite the hook failure.");

    /// <summary>
    ///     The delegate that writes a dead-lettering post-dispatch hook failure event.
    /// </summary>
    private static readonly Action<ILogger, Guid, Exception> AfterDispatchDeadLetteredMessage =
        LoggerMessage.Define<Guid>(
            LogLevel.Warning,
            new EventId(2005, nameof(AfterDispatchDeadLettered)),
            "The outbox AfterDispatch hook failed for message {MessageId}; moving the message to dead letter.");

    /// <summary>
    ///     The delegate that writes a skipped terminal persistence event.
    /// </summary>
    private static readonly Action<ILogger, Guid, Exception?> TerminalPersistSkippedMessage =
        LoggerMessage.Define<Guid>(
            LogLevel.Warning,
            new EventId(2006, nameof(TerminalPersistSkipped)),
            "Outbox terminal persistence was skipped for message {MessageId} because the active lease was lost.");

    /// <summary>
    ///     The delegate that writes the outbox processor pass completion event.
    /// </summary>
    private static readonly Action<ILogger, double, int, int, int, int, Exception?> PassCompletedMessage =
        LoggerMessage.Define<double, int, int, int, int>(
            LogLevel.Information,
            new EventId(2007, nameof(PassCompleted)),
            "Outbox pass completed in {ElapsedMilliseconds} ms. Leased={LeasedCount}, Published={PublishedCount}, Failed={FailedCount}, DeadLettered={DeadLetteredCount}.");

    /// <summary>
    ///     Logs that the outbox processor loop failed before the next pass.
    /// </summary>
    /// <param name="logger">The logger receiving the event.</param>
    /// <param name="exception">The exception that aborted the pass.</param>
    public static void LoopFailed(ILogger logger, Exception exception)
    {
        LoopFailedMessage(logger, exception);
    }

    /// <summary>
    ///     Logs an outbox dispatch failure.
    /// </summary>
    /// <param name="logger">The logger receiving the event.</param>
    /// <param name="messageId">The message that failed dispatch.</param>
    /// <param name="attemptCount">The current dispatch attempt.</param>
    /// <param name="exception">The dispatch exception.</param>
    public static void DispatchFailed(ILogger logger, Guid messageId, int attemptCount, Exception exception)
    {
        DispatchFailedMessage(logger, messageId, attemptCount, exception);
    }

    /// <summary>
    ///     Logs skipped persistence after lease loss.
    /// </summary>
    /// <param name="logger">The logger receiving the event.</param>
    /// <param name="messageId">The message whose retry outcome was skipped.</param>
    public static void LeaseLossPersistSkipped(ILogger logger, Guid messageId)
    {
        LeaseLossPersistSkippedMessage(logger, messageId, null);
    }

    /// <summary>
    ///     Logs a post-dispatch hook failure that does not prevent completion.
    /// </summary>
    /// <param name="logger">The logger receiving the event.</param>
    /// <param name="messageId">The dispatched message.</param>
    /// <param name="exception">The hook exception.</param>
    public static void AfterDispatchCompleted(ILogger logger, Guid messageId, Exception exception)
    {
        AfterDispatchCompletedMessage(logger, messageId, exception);
    }

    /// <summary>
    ///     Logs a post-dispatch hook failure that moves the message to dead letter.
    /// </summary>
    /// <param name="logger">The logger receiving the event.</param>
    /// <param name="messageId">The dispatched message.</param>
    /// <param name="exception">The hook exception.</param>
    public static void AfterDispatchDeadLettered(ILogger logger, Guid messageId, Exception exception)
    {
        AfterDispatchDeadLetteredMessage(logger, messageId, exception);
    }

    /// <summary>
    ///     Logs skipped terminal persistence after lease loss.
    /// </summary>
    /// <param name="logger">The logger receiving the event.</param>
    /// <param name="messageId">The message whose terminal outcome was skipped.</param>
    public static void TerminalPersistSkipped(ILogger logger, Guid messageId)
    {
        TerminalPersistSkippedMessage(logger, messageId, null);
    }

    /// <summary>
    ///     Logs a completed outbox processor pass.
    /// </summary>
    /// <param name="logger">The logger receiving the event.</param>
    /// <param name="elapsedMilliseconds">The elapsed pass duration in milliseconds.</param>
    /// <param name="leasedCount">The number of leased messages.</param>
    /// <param name="publishedCount">The number of published messages.</param>
    /// <param name="failedCount">The number of retryable failures.</param>
    /// <param name="deadLetteredCount">The number of dead-lettered messages.</param>
    public static void PassCompleted(
        ILogger logger,
        double elapsedMilliseconds,
        int leasedCount,
        int publishedCount,
        int failedCount,
        int deadLetteredCount)
    {
        PassCompletedMessage(
            logger,
            elapsedMilliseconds,
            leasedCount,
            publishedCount,
            failedCount,
            deadLetteredCount,
            null);
    }
}
