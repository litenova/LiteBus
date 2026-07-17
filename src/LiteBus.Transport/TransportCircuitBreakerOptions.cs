namespace LiteBus.Transport;

/// <summary>
///     Configures one transport circuit breaker instance.
/// </summary>
public sealed record TransportCircuitBreakerOptions
{
    /// <summary>
    ///     Gets the number of consecutive failures required to open the circuit.
    /// </summary>
    /// <value>
    ///     When zero, the circuit breaker is disabled.
    /// </value>
    public int FailureThreshold { get; init; } = 5;

    /// <summary>
    ///     Gets how long broker operations are rejected after the circuit opens.
    /// </summary>
    public TimeSpan BreakDuration { get; init; } = TimeSpan.FromSeconds(30);
}
