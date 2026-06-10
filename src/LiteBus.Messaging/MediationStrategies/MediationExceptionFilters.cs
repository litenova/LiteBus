using System;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Messaging.MediationStrategies;

/// <summary>
///     Identifies exceptions that should propagate from mediation instead of being routed to error handlers.
/// </summary>
internal static class MediationExceptionFilters
{
    /// <summary>
    ///     Determines whether an exception should be handled by registered error handlers.
    /// </summary>
    /// <param name="exception">The exception raised during mediation.</param>
    /// <returns>
    ///     <see langword="true" /> when error handlers may run; <see langword="false" /> when the exception must propagate.
    /// </returns>
    public static bool IsRecoverableMediationException(Exception exception)
    {
        return exception is not LiteBusExecutionAbortedException
            and not NoHandlerFoundException
            and not MultipleHandlerFoundException;
    }
}
