using System;

namespace LiteBus.Transport.Amqp;

/// <summary>
///     Configures AMQP connection and per-exchange publisher circuit breakers.
/// </summary>
/// <remarks>
///     The transport module copies these values into the connection breaker and each publisher circuit.
/// </remarks>
public sealed record AmqpCircuitBreakerOptions
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

    /// <summary>
    ///     Converts AMQP circuit breaker settings to the shared transport options type.
    /// </summary>
    /// <returns>The transport circuit breaker options.</returns>
    internal TransportCircuitBreakerOptions ToTransportOptions()
    {
        return new TransportCircuitBreakerOptions
        {
            FailureThreshold = FailureThreshold,
            BreakDuration = BreakDuration
        };
    }
}
