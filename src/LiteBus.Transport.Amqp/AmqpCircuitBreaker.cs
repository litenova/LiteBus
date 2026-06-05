using LiteBus.Transport;

namespace LiteBus.Transport.Amqp;

/// <summary>
///     Tracks consecutive AMQP failures and temporarily rejects new operations when a threshold is exceeded.
/// </summary>
/// <remarks>
///     This type aliases <see cref="TransportCircuitBreaker" /> for backward-compatible AMQP call sites.
/// </remarks>
public sealed class AmqpCircuitBreaker : TransportCircuitBreaker
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="AmqpCircuitBreaker" /> class.
    /// </summary>
    /// <param name="options">The circuit breaker settings.</param>
    public AmqpCircuitBreaker(AmqpCircuitBreakerOptions? options = null)
        : base((options ?? new AmqpCircuitBreakerOptions()).ToTransportOptions())
    {
    }

    /// <inheritdoc />
    public new void ThrowIfOpen()
    {
        try
        {
            base.ThrowIfOpen();
        }
        catch (TransportCircuitBreakerOpenException)
        {
            throw new AmqpCircuitBreakerOpenException();
        }
    }
}
