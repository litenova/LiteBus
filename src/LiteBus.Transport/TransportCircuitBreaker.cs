namespace LiteBus.Transport;

/// <summary>
///     Tracks consecutive transport failures and temporarily rejects new operations when a threshold is exceeded.
/// </summary>
/// <remarks>
///     After the break duration, one caller is admitted as a recovery probe. Other callers remain rejected until that
///     probe records success or failure.
/// </remarks>
public class TransportCircuitBreaker : ITransportCircuitBreaker
{
    /// <summary>
    ///     Identifies a circuit that is accepting operations normally.
    /// </summary>
    private const int ClosedState = 0;

    /// <summary>
    ///     Identifies a circuit that is rejecting operations for the configured break duration.
    /// </summary>
    private const int OpenState = 1;

    /// <summary>
    ///     Identifies a circuit that has admitted one recovery probe and rejects sibling probes.
    /// </summary>
    private const int HalfOpenState = 2;

    /// <summary>
    ///     Gets the circuit breaker settings.
    /// </summary>
    private readonly TransportCircuitBreakerOptions _options;

    /// <summary>
    ///     Serializes circuit state transitions and half-open probe admission.
    /// </summary>
    private readonly object _stateSync = new();

    /// <summary>
    ///     Gets the monotonic time source used to measure break durations.
    /// </summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>
    ///     Gets the number of consecutive failures observed while the circuit is closed.
    /// </summary>
    private int _consecutiveFailures;

    /// <summary>
    ///     Gets the generation used to reject outcomes from operations admitted before a state transition.
    /// </summary>
    private long _generation = 1;

    /// <summary>
    ///     Gets the monotonic timestamp recorded when the circuit most recently opened.
    /// </summary>
    private long _openedAtTimestamp;

    /// <summary>
    ///     Gets the current closed, open, or half-open state.
    /// </summary>
    private int _state;

    /// <summary>
    ///     Initializes a new instance of the <see cref="TransportCircuitBreaker" /> class.
    /// </summary>
    /// <param name="options">The circuit breaker settings.</param>
    /// <param name="timeProvider">The monotonic time source used to measure break durations.</param>
    public TransportCircuitBreaker(
        TransportCircuitBreakerOptions? options = null,
        TimeProvider? timeProvider = null)
    {
        _options = options ?? new TransportCircuitBreakerOptions();
        _timeProvider = timeProvider ?? TimeProvider.System;

        ArgumentOutOfRangeException.ThrowIfNegative(_options.FailureThreshold);

        if (_options.FailureThreshold > 0)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(_options.BreakDuration.Ticks);
        }
    }

    /// <inheritdoc />
    public bool IsOpen
    {
        get
        {
            lock (_stateSync)
            {
                return IsEnabled() && _state != ClosedState;
            }
        }
    }

    /// <inheritdoc />
    public int FailureCount
    {
        get
        {
            lock (_stateSync)
            {
                return _state == ClosedState
                    ? _consecutiveFailures
                    : _options.FailureThreshold;
            }
        }
    }

    /// <inheritdoc />
    public TransportCircuitBreakerPermit AcquirePermit()
    {
        lock (_stateSync)
        {
            if (!IsEnabled() || _state == ClosedState)
            {
                return new TransportCircuitBreakerPermit(_generation, false);
            }

            if (_state == HalfOpenState)
            {
                throw new TransportCircuitBreakerOpenException();
            }

            if (_timeProvider.GetElapsedTime(_openedAtTimestamp) < _options.BreakDuration)
            {
                throw new TransportCircuitBreakerOpenException();
            }

            _state = HalfOpenState;
            return new TransportCircuitBreakerPermit(_generation, true);
        }
    }

    /// <inheritdoc />
    public void RecordSuccess(TransportCircuitBreakerPermit permit)
    {
        lock (_stateSync)
        {
            if (!IsEnabled() || permit.Generation != _generation)
            {
                return;
            }

            if (_state == OpenState || (_state == HalfOpenState && !permit.IsRecoveryProbe))
            {
                return;
            }

            _consecutiveFailures = 0;
            _state = ClosedState;
            _openedAtTimestamp = 0;

            if (permit.IsRecoveryProbe)
            {
                _generation++;
            }
        }
    }

    /// <inheritdoc />
    public void RecordFailure(TransportCircuitBreakerPermit permit)
    {
        lock (_stateSync)
        {
            if (!IsEnabled() || permit.Generation != _generation || _state == OpenState)
            {
                return;
            }

            if (_state == HalfOpenState)
            {
                if (permit.IsRecoveryProbe)
                {
                    OpenCircuit();
                }

                return;
            }

            _consecutiveFailures++;
            TransportCircuitBreakerTelemetry.RecordFailureObserved(_consecutiveFailures);

            if (_consecutiveFailures >= _options.FailureThreshold)
            {
                OpenCircuit();
            }
        }
    }

    /// <summary>
    ///     Transitions the circuit to open without extending an already-open deadline.
    /// </summary>
    private void OpenCircuit()
    {
        _consecutiveFailures = _options.FailureThreshold;
        _openedAtTimestamp = _timeProvider.GetTimestamp();
        _state = OpenState;
        _generation++;
        TransportCircuitBreakerTelemetry.RecordCircuitOpened();
    }

    /// <summary>
    ///     Determines whether circuit breaker enforcement is active.
    /// </summary>
    /// <returns><see langword="true" /> when failures should open the circuit; otherwise, <see langword="false" />.</returns>
    private bool IsEnabled()
    {
        return _options.FailureThreshold > 0;
    }
}
