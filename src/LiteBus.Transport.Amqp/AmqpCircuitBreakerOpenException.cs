using System;

namespace LiteBus.Transport.Amqp;

/// <summary>
///     Thrown when AMQP operations are rejected because the circuit breaker is open.
/// </summary>
public sealed class AmqpCircuitBreakerOpenException : Exception
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="AmqpCircuitBreakerOpenException" /> class.
    /// </summary>
    public AmqpCircuitBreakerOpenException()
        : base("The AMQP circuit breaker is open because recent broker operations failed.")
    {
    }
}
