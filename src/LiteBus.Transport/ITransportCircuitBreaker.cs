namespace LiteBus.Transport;

/// <summary>
///     Exposes circuit breaker state and lifecycle hooks for transport adapters.
/// </summary>
public interface ITransportCircuitBreaker
{
    /// <summary>
    ///     Gets a value indicating whether the circuit is currently open and rejecting operations.
    /// </summary>
    /// <value><see langword="true" /> when new operations should be rejected; otherwise, <see langword="false" />.</value>
    bool IsOpen { get; }

    /// <summary>
    ///     Gets the number of consecutive failures recorded while the circuit is closed.
    /// </summary>
    /// <value>The current consecutive failure count.</value>
    int FailureCount { get; }

    /// <summary>
    ///     Throws <see cref="TransportCircuitBreakerOpenException" /> when the circuit is open.
    /// </summary>
    void ThrowIfOpen();

    /// <summary>
    ///     Records a successful transport operation and resets failure tracking.
    /// </summary>
    void RecordSuccess();

    /// <summary>
    ///     Records a failed transport operation and opens the circuit when the failure threshold is reached.
    /// </summary>
    void RecordFailure();
}
