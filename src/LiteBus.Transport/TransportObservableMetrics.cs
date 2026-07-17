using System.Diagnostics.Metrics;
using System.Threading;

namespace LiteBus.Transport;

/// <summary>
///     Registers observable OpenTelemetry gauges for transport circuit breaker state.
/// </summary>
public sealed class TransportObservableMetrics : IDisposable
{
    /// <summary>
    ///     The meter retained for the lifetime of this metrics registrar.
    /// </summary>
    private readonly Meter _meter;

    /// <summary>
    ///     The service provider used to resolve the shared circuit breaker at observation time.
    /// </summary>
    private readonly IServiceProvider _serviceProvider;

    /// <summary>
    ///     Tracks whether the meter has been disposed.
    /// </summary>
    private int _disposeState;

    /// <summary>
    ///     Initializes a new instance of the <see cref="TransportObservableMetrics" /> class.
    /// </summary>
    /// <param name="serviceProvider">The service provider used to resolve transport dependencies.</param>
    public TransportObservableMetrics(IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        _serviceProvider = serviceProvider;

        _meter = new Meter(LiteBusTransportTelemetry.MeterName);

        _meter.CreateObservableGauge(
            LiteBusTransportTelemetry.CircuitBreakerOpenInstrumentName,
            ObserveCircuitBreakerOpen,
            description: "Whether the transport circuit breaker is open where 1 is open and 0 is closed.");

        _meter.CreateObservableGauge(
            LiteBusTransportTelemetry.CircuitBreakerFailureCountInstrumentName,
            ObserveCircuitBreakerFailureCount,
            description: "The current consecutive transport failure count observed by the circuit breaker.");
    }

    /// <summary>
    ///     Disposes the meter and unregisters its observable instruments.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) == 0)
        {
            _meter.Dispose();
        }
    }

    /// <summary>
    ///     Observes whether the transport circuit breaker is currently open.
    /// </summary>
    /// <returns>The circuit breaker open measurement, if a circuit breaker is registered.</returns>
    private IEnumerable<Measurement<int>> ObserveCircuitBreakerOpen()
    {
        var circuitBreaker = ResolveCircuitBreaker();

        if (circuitBreaker is null)
        {
            yield break;
        }

        yield return new Measurement<int>(
            circuitBreaker.IsOpen ? 1 : 0,
            CreateBrokerTags());
    }

    /// <summary>
    ///     Observes the current transport circuit breaker failure count.
    /// </summary>
    /// <returns>The circuit breaker failure count measurement, if a circuit breaker is registered.</returns>
    private IEnumerable<Measurement<long>> ObserveCircuitBreakerFailureCount()
    {
        var circuitBreaker = ResolveCircuitBreaker();

        if (circuitBreaker is null)
        {
            yield break;
        }

        yield return new Measurement<long>(
            circuitBreaker.FailureCount,
            CreateBrokerTags());
    }

    /// <summary>
    ///     Creates the broker dimension tags applied to circuit breaker measurements.
    /// </summary>
    /// <returns>The broker tag collection when a broker identity is registered.</returns>
    private KeyValuePair<string, object?>[] CreateBrokerTags()
    {
        if (_serviceProvider.GetService(typeof(TransportBrokerIdentity)) is TransportBrokerIdentity identity)
        {
            return
            [
                new KeyValuePair<string, object?>(LiteBusTransportTelemetry.BrokerTagName, identity.Broker)
            ];
        }

        return [];
    }

    /// <summary>
    ///     Resolves the circuit breaker from the service provider, when available.
    /// </summary>
    /// <returns>The circuit breaker instance, or <see langword="null" /> when transport is not registered.</returns>
    private ITransportCircuitBreaker? ResolveCircuitBreaker()
    {
        return _serviceProvider.GetService(typeof(ITransportCircuitBreaker)) as ITransportCircuitBreaker;
    }
}
