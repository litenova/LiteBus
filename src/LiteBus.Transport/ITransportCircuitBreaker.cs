namespace LiteBus.Transport;

/// <summary>
///     Exposes circuit breaker state and lifecycle hooks for transport adapters.
/// </summary>
public interface ITransportCircuitBreaker
{
    /// <summary>
    ///     Gets a value indicating whether the circuit is open or has admitted a half-open recovery probe.
    /// </summary>
    /// <value>
    ///     <see langword="true" /> when the circuit is open or half-open; otherwise, <see langword="false" />.
    /// </value>
    bool IsOpen { get; }

    /// <summary>
    ///     Gets the number of consecutive failures recorded while the circuit is closed, or the configured failure
    ///     threshold while the circuit is open.
    /// </summary>
    /// <value>The current failure count exposed to operators and telemetry.</value>
    int FailureCount { get; }

    /// <summary>
    ///     Acquires permission to start one transport operation.
    /// </summary>
    /// <returns>An opaque permit that must be supplied when recording the operation outcome.</returns>
    /// <exception cref="TransportCircuitBreakerOpenException">The circuit is open or another recovery probe is active.</exception>
    TransportCircuitBreakerPermit AcquirePermit();

    /// <summary>
    ///     Records a successful transport operation and resets failure tracking.
    /// </summary>
    /// <param name="permit">The permit that admitted the completed operation.</param>
    void RecordSuccess(TransportCircuitBreakerPermit permit);

    /// <summary>
    ///     Records a failed transport operation and opens the circuit when the failure threshold is reached.
    /// </summary>
    /// <param name="permit">The permit that admitted the failed operation.</param>
    void RecordFailure(TransportCircuitBreakerPermit permit);

    /// <summary>
    ///     Releases an admitted operation whose outcome does not indicate broker health.
    /// </summary>
    /// <remarks>
    ///     Publishers use this for caller cancellation and failures that occur outside broker I/O. Releasing a
    ///     half-open recovery probe allows another caller to test broker recovery without closing the circuit or
    ///     starting a new break duration.
    /// </remarks>
    /// <param name="permit">The permit that admitted the operation.</param>
    void ReleasePermit(TransportCircuitBreakerPermit permit);
}
