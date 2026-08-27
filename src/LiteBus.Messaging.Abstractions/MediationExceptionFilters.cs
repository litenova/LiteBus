using System;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Classifies exceptions raised during mediation, so the pipeline and the durable processors treat a decision
///     differently from a fault.
/// </summary>
public static class MediationExceptionFilters
{
    /// <summary>
    ///     Determines whether an exception should be handled by registered error handlers.
    /// </summary>
    /// <param name="exception">The exception raised during mediation.</param>
    /// <returns>
    ///     <see langword="true" /> when error handlers may run; <see langword="false" /> when the exception must propagate.
    /// </returns>
    /// <remarks>
    ///     Handler resolution failures are configuration errors rather than message failures, cancellation is the caller's
    ///     decision, and a refusal is the pipeline's own decision rather than a fault, so all of them propagate unchanged.
    ///     Routing a refusal to error handlers would let a handler written for failures recover from one.
    /// </remarks>
    public static bool IsRecoverableMediationException(Exception exception)
    {
        return exception is not NoHandlerFoundException
            and not MultipleHandlerFoundException
            and not OperationCanceledException
            && !IsRefusal(exception);
    }

    /// <summary>
    ///     Determines whether an exception carries a pre-stage refusal rather than a fault.
    /// </summary>
    /// <param name="exception">The exception raised during mediation.</param>
    /// <returns><see langword="true" /> for a guard denial or a validation failure.</returns>
    /// <remarks>
    ///     A refusal is a decision the pipeline made about the message itself, so it is reproducible: the same message
    ///     refused once is refused every time. Durable processors use this to retire such a message on its first attempt
    ///     rather than spending the retry schedule on an answer that cannot change.
    /// </remarks>
    public static bool IsRefusal(Exception exception)
    {
        return exception is LiteBusMessageDeniedException or LiteBusMessageInvalidException;
    }

    /// <summary>
    ///     Determines whether a failed dispatch is worth attempting again.
    /// </summary>
    /// <param name="exception">The exception that ended the dispatch.</param>
    /// <returns>
    ///     <see langword="false" /> for a refusal or a configuration error, which produce the same outcome on every
    ///     attempt; otherwise <see langword="true" />.
    /// </returns>
    /// <remarks>
    ///     Retrying exists for transient conditions such as a dropped connection or a locked row. A message no handler
    ///     is registered for, or one a guard refuses, fails identically on every attempt, so retrying it delays the
    ///     dead-letter entry an operator is waiting to see and occupies the processor while doing it.
    /// </remarks>
    public static bool IsRetryableDispatchException(Exception exception)
    {
        return exception is not NoHandlerFoundException
            and not MultipleHandlerFoundException
            && !IsRefusal(exception);
    }
}
