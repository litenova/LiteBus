namespace LiteBus.Transport;

/// <summary>
///     Identifies one operation admitted by a transport circuit breaker.
/// </summary>
/// <remarks>
///     Callers obtain permits from <see cref="ITransportCircuitBreaker.AcquirePermit" /> and return the same value when
///     recording the operation outcome. The generation is intentionally opaque so stale completions cannot mutate a
///     newer open or half-open state.
/// </remarks>
public readonly struct TransportCircuitBreakerPermit
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="TransportCircuitBreakerPermit" /> struct.
    /// </summary>
    /// <param name="generation">The circuit generation that admitted the operation.</param>
    /// <param name="isRecoveryProbe">Whether the operation is the single half-open recovery probe.</param>
    internal TransportCircuitBreakerPermit(long generation, bool isRecoveryProbe)
    {
        Generation = generation;
        IsRecoveryProbe = isRecoveryProbe;
    }

    /// <summary>
    ///     Gets the circuit generation that admitted the operation.
    /// </summary>
    internal long Generation { get; }

    /// <summary>
    ///     Gets a value indicating whether the operation is the single half-open recovery probe.
    /// </summary>
    internal bool IsRecoveryProbe { get; }
}
