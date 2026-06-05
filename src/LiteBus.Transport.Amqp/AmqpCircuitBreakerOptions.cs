using System;
using LiteBus.Transport;

namespace LiteBus.Transport.Amqp;

/// <summary>
///     Configures AMQP circuit breaker behavior shared by connection and publish operations.
/// </summary>
/// <remarks>
///     Maps to <see cref="TransportCircuitBreakerOptions" /> used by the shared transport circuit breaker.
/// </remarks>
public sealed class AmqpCircuitBreakerOptions
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
    internal TransportCircuitBreakerOptions ToTransportOptions() =>
        new()
        {
            FailureThreshold = FailureThreshold,
            BreakDuration = BreakDuration
        };
}
