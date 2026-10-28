using System;

namespace LiteBus.Transport;

/// <summary>
///     Thrown when transport operations are rejected because the circuit breaker is open.
/// </summary>
public class TransportCircuitBreakerOpenException : Exception
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="TransportCircuitBreakerOpenException" /> class.
    /// </summary>
    public TransportCircuitBreakerOpenException()
        : base("The transport circuit breaker is open because recent broker operations failed.")
    {
    }
}
