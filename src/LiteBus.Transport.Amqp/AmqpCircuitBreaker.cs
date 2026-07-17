using System;

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
        : this(options, TimeProvider.System)
    {
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="AmqpCircuitBreaker" /> class.
    /// </summary>
    /// <param name="options">The circuit breaker settings.</param>
    /// <param name="timeProvider">The monotonic time source used to measure break durations.</param>
    public AmqpCircuitBreaker(AmqpCircuitBreakerOptions? options, TimeProvider timeProvider)
        : base((options ?? new AmqpCircuitBreakerOptions()).ToTransportOptions(), timeProvider)
    {
    }

    /// <summary>
    ///     Acquires an operation permit, translating the shared transport exception to the AMQP-specific type.
    /// </summary>
    /// <returns>The permit that identifies the admitted operation.</returns>
    public new TransportCircuitBreakerPermit AcquirePermit()
    {
        try
        {
            return base.AcquirePermit();
        }
        catch (TransportCircuitBreakerOpenException)
        {
            throw new AmqpCircuitBreakerOpenException();
        }
    }
}
