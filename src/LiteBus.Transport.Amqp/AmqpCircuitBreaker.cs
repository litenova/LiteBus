using System;
using System.Threading;

namespace LiteBus.Transport.Amqp;

/// <summary>
///     Tracks consecutive AMQP failures and temporarily rejects new operations when a threshold is exceeded.
/// </summary>
public sealed class AmqpCircuitBreaker
{
    /// <summary>
    ///     Gets the circuit breaker settings.
    /// </summary>
    private readonly AmqpCircuitBreakerOptions _options;

    /// <summary>
    ///     Gets the number of consecutive failures observed while the circuit is closed.
    /// </summary>
    private int _consecutiveFailures;

    /// <summary>
    ///     Gets the tick count after which the circuit closes again, or zero when the circuit is closed.
    /// </summary>
    private long _openUntilTicks;

    /// <summary>
    ///     Initializes a new instance of the <see cref="AmqpCircuitBreaker" /> class.
    /// </summary>
    /// <param name="options">The circuit breaker settings.</param>
    public AmqpCircuitBreaker(AmqpCircuitBreakerOptions? options = null)
    {
        _options = options ?? new AmqpCircuitBreakerOptions();
    }

    /// <summary>
    ///     Throws <see cref="AmqpCircuitBreakerOpenException" /> when the circuit is open.
    /// </summary>
    public void ThrowIfOpen()
    {
        if (!IsEnabled())
        {
            return;
        }

        var openUntilTicks = Volatile.Read(ref _openUntilTicks);

        if (openUntilTicks == 0)
        {
            return;
        }

        if (Environment.TickCount64 < openUntilTicks)
        {
            throw new AmqpCircuitBreakerOpenException();
        }

        Volatile.Write(ref _openUntilTicks, 0);
        Interlocked.Exchange(ref _consecutiveFailures, 0);
    }

    /// <summary>
    ///     Records a successful AMQP operation and resets failure tracking.
    /// </summary>
    public void RecordSuccess()
    {
        if (!IsEnabled())
        {
            return;
        }

        Interlocked.Exchange(ref _consecutiveFailures, 0);
        Volatile.Write(ref _openUntilTicks, 0);
    }

    /// <summary>
    ///     Records a failed AMQP operation and opens the circuit when the failure threshold is reached.
    /// </summary>
    public void RecordFailure()
    {
        if (!IsEnabled())
        {
            return;
        }

        var failures = Interlocked.Increment(ref _consecutiveFailures);

        if (failures < _options.FailureThreshold)
        {
            return;
        }

        Volatile.Write(
            ref _openUntilTicks,
            Environment.TickCount64 + (long)_options.BreakDuration.TotalMilliseconds);
        Interlocked.Exchange(ref _consecutiveFailures, 0);
    }

    /// <summary>
    ///     Determines whether circuit breaker enforcement is active.
    /// </summary>
    /// <returns><see langword="true" /> when failures should open the circuit; otherwise, <see langword="false" />.</returns>
    private bool IsEnabled()
    {
        return _options.FailureThreshold > 0 && _options.BreakDuration > TimeSpan.Zero;
    }
}
