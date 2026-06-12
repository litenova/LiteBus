namespace LiteBus.Transport;

/// <summary>
///     Tracks consecutive transport failures and temporarily rejects new operations when a threshold is exceeded.
/// </summary>
public class TransportCircuitBreaker : ITransportCircuitBreaker
{
    /// <summary>
    ///     Gets the circuit breaker settings.
    /// </summary>
    private readonly TransportCircuitBreakerOptions _options;

    /// <summary>
    ///     Gets the number of consecutive failures observed while the circuit is closed.
    /// </summary>
    private int _consecutiveFailures;

    /// <summary>
    ///     Gets the tick count after which the circuit closes again, or zero when the circuit is closed.
    /// </summary>
    private long _openUntilTicks;

    /// <summary>
    ///     Initializes a new instance of the <see cref="TransportCircuitBreaker" /> class.
    /// </summary>
    /// <param name="options">The circuit breaker settings.</param>
    public TransportCircuitBreaker(TransportCircuitBreakerOptions? options = null)
    {
        _options = options ?? new TransportCircuitBreakerOptions();
    }

    /// <inheritdoc />
    public bool IsOpen
    {
        get
        {
            if (!IsEnabled())
            {
                return false;
            }

            var openUntilTicks = Volatile.Read(ref _openUntilTicks);

            return openUntilTicks != 0 && Environment.TickCount64 < openUntilTicks;
        }
    }

    /// <inheritdoc />
    public int FailureCount
    {
        get
        {
            var failures = Volatile.Read(ref _consecutiveFailures);

            if (failures > 0)
            {
                return failures;
            }

            if (!IsEnabled())
            {
                return 0;
            }

            var openUntilTicks = Volatile.Read(ref _openUntilTicks);

            return openUntilTicks != 0 && Environment.TickCount64 < openUntilTicks
                ? _options.FailureThreshold
                : 0;
        }
    }

    /// <inheritdoc />
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
            throw new TransportCircuitBreakerOpenException();
        }

        Volatile.Write(ref _openUntilTicks, 0);
        Interlocked.Exchange(ref _consecutiveFailures, 0);
    }

    /// <inheritdoc />
    public void RecordSuccess()
    {
        if (!IsEnabled())
        {
            return;
        }

        Interlocked.Exchange(ref _consecutiveFailures, 0);
        Volatile.Write(ref _openUntilTicks, 0);
    }

    /// <inheritdoc />
    public void RecordFailure()
    {
        if (!IsEnabled())
        {
            return;
        }

        var failures = Interlocked.Increment(ref _consecutiveFailures);
        TransportCircuitBreakerTelemetry.RecordFailureObserved(failures);

        if (failures < _options.FailureThreshold)
        {
            return;
        }

        Volatile.Write(
            ref _openUntilTicks,
            Environment.TickCount64 + (long) _options.BreakDuration.TotalMilliseconds);

        TransportCircuitBreakerTelemetry.RecordCircuitOpened();
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