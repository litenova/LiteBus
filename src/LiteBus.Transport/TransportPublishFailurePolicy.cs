namespace LiteBus.Transport;

/// <summary>
///     Determines whether a publish failure should increment the transport circuit breaker.
/// </summary>
public static class TransportPublishFailurePolicy
{
    /// <summary>
    ///     Returns a value indicating whether the circuit breaker should record a publish failure.
    /// </summary>
    /// <param name="exception">The exception observed during publish.</param>
    /// <returns>
    ///     <see langword="true" /> for broker and application failures; <see langword="false" /> for caller-initiated
    ///     cancellation.
    /// </returns>
    public static bool ShouldRecordFailure(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return exception is not OperationCanceledException;
    }

    /// <summary>
    ///     Records a circuit breaker failure when the thrown exception should count against transport connectivity.
    /// </summary>
    /// <param name="circuitBreaker">The circuit breaker guarding the operation, if any.</param>
    /// <param name="exception">The exception observed during publish or connection open.</param>
    public static void RecordFailureIfApplicable(ITransportCircuitBreaker? circuitBreaker, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (circuitBreaker is not null && ShouldRecordFailure(exception))
        {
            circuitBreaker.RecordFailure();
        }
    }
}
