using System;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Identifies exceptions that should propagate from mediation instead of being routed to error handlers.
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
    ///     decision, and a denial is the pipeline's own decision rather than a fault, so all of them propagate unchanged.
    ///     Routing a denial to error handlers would let a handler written for failures recover from a refusal.
    /// </remarks>
    public static bool IsRecoverableMediationException(Exception exception)
    {
        return exception is not NoHandlerFoundException
            and not MultipleHandlerFoundException
            and not OperationCanceledException
            and not LiteBusMessageDeniedException;
    }
}
