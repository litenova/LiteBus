using System;
using System.Threading;

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
    ///     decision, a refusal is the pipeline's own decision rather than a fault, and an idempotency misconfiguration
    ///     is a declaration mistake rather than a message failure, so all of them propagate unchanged.
    ///     Routing a refusal to error handlers would let a handler written for failures recover from one.
    /// </remarks>
    public static bool IsRecoverableMediationException(Exception exception)
    {
        return exception is not NoHandlerFoundException
            and not MultipleHandlerFoundException
            and not OperationCanceledException
            and not IdempotencyConfigurationException
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
    ///     <para>
    ///         The inner exception chain is inspected as well, so a dispatcher that wraps a refusal or a missing handler in
    ///         its own exception type still has it retired on the first attempt.
    ///     </para>
    /// </remarks>
    public static bool IsRetryableDispatchException(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is NoHandlerFoundException or MultipleHandlerFoundException || IsRefusal(current))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    ///     Determines whether an exception reports the caller's own cancellation rather than a dispatch failure.
    /// </summary>
    /// <param name="exception">The exception that ended the dispatch.</param>
    /// <param name="cancellationToken">The token the caller passed to the dispatch.</param>
    /// <returns>
    ///     <see langword="true" /> when <paramref name="cancellationToken" /> is cancelled and the exception, or one of
    ///     its inner exceptions, is an <see cref="OperationCanceledException" />.
    /// </returns>
    /// <remarks>
    ///     Durable processors use this to leave a cancelled message untouched: a host shutdown, a drain, or a dispatch
    ///     timeout must not consume an attempt, record an error, or move the message's next visibility. The inner chain is
    ///     inspected because a dispatcher may wrap the cancellation in its own exception type. A cancellation raised by
    ///     a token other than the caller's is a failure like any other and returns <see langword="false" />.
    /// </remarks>
    public static bool IsCallerCancellation(Exception exception, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }

        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is OperationCanceledException)
            {
                return true;
            }
        }

        return false;
    }
}
