using LiteBus.Transport;

namespace LiteBus.Transport.Amqp;

/// <summary>
///     Thrown when AMQP operations are rejected because the circuit breaker is open.
/// </summary>
public sealed class AmqpCircuitBreakerOpenException : TransportCircuitBreakerOpenException
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="AmqpCircuitBreakerOpenException" /> class.
    /// </summary>
    public AmqpCircuitBreakerOpenException()
        : base()
    {
    }
}
