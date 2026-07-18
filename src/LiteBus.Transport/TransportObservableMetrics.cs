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
    ///     The service provider used to resolve the circuit breaker registry at observation time.
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
            description: "Whether any publisher circuit is open where 1 is open and 0 is closed.");

        _meter.CreateObservableGauge(
            LiteBusTransportTelemetry.CircuitBreakerFailureCountInstrumentName,
            ObserveCircuitBreakerFailureCount,
            description: "The sum of current consecutive failures across publisher circuits.");
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
        var registry = ResolveCircuitBreakerRegistry();

        if (registry is null)
        {
            yield break;
        }

        yield return new Measurement<int>(
            registry.IsAnyOpen ? 1 : 0,
            CreateBrokerTags());
    }

    /// <summary>
    ///     Observes the current transport circuit breaker failure count.
    /// </summary>
    /// <returns>The circuit breaker failure count measurement, if a circuit breaker is registered.</returns>
    private IEnumerable<Measurement<long>> ObserveCircuitBreakerFailureCount()
    {
        var registry = ResolveCircuitBreakerRegistry();

        if (registry is null)
        {
            yield break;
        }

        yield return new Measurement<long>(
            registry.FailureCount,
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
    ///     Resolves the circuit breaker registry from the service provider, when available.
    /// </summary>
    /// <returns>The circuit breaker registry, or <see langword="null" /> when transport is not registered.</returns>
    private ITransportCircuitBreakerRegistry? ResolveCircuitBreakerRegistry()
    {
        return _serviceProvider.GetService(typeof(ITransportCircuitBreakerRegistry)) as ITransportCircuitBreakerRegistry;
    }
}
