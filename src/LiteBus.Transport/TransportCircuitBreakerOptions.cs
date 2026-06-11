namespace LiteBus.Transport;

/// <summary>
///     Configures transport circuit breaker behavior shared by connection and publish operations.
/// </summary>
public sealed class TransportCircuitBreakerOptions
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