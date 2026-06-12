using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;

namespace LiteBus.Transport.Amqp;

/// <summary>
///     Registers observable OpenTelemetry gauges for AMQP circuit breaker state.
/// </summary>
public sealed class AmqpTransportObservableMetrics
{
    /// <summary>
    ///     The service provider used to resolve the shared AMQP connection manager at observation time.
    /// </summary>
    private readonly IServiceProvider _serviceProvider;

    /// <summary>
    ///     Initializes a new instance of the <see cref="AmqpTransportObservableMetrics" /> class.
    /// </summary>
    /// <param name="serviceProvider">The service provider used to resolve AMQP transport dependencies.</param>
    public AmqpTransportObservableMetrics(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));

        var meter = new Meter(LiteBusTransportAmqpTelemetry.MeterName);

        meter.CreateObservableGauge(
            LiteBusTransportAmqpTelemetry.CircuitBreakerOpenInstrumentName,
            ObserveCircuitBreakerOpen,
            description: "Whether the AMQP circuit breaker is open where 1 is open and 0 is closed.");

        meter.CreateObservableGauge(
            LiteBusTransportAmqpTelemetry.CircuitBreakerFailureCountInstrumentName,
            ObserveCircuitBreakerFailureCount,
            description: "The current consecutive AMQP failure count observed by the circuit breaker.");
    }

    /// <summary>
    ///     Observes whether the AMQP circuit breaker is currently open.
    /// </summary>
    /// <returns>The circuit breaker open measurement, if a connection manager is registered.</returns>
    private IEnumerable<Measurement<int>> ObserveCircuitBreakerOpen()
    {
        var circuitBreaker = ResolveCircuitBreaker();

        if (circuitBreaker is null)
        {
            yield break;
        }

        yield return new Measurement<int>(circuitBreaker.IsOpen ? 1 : 0);
    }

    /// <summary>
    ///     Observes the current AMQP circuit breaker failure count.
    /// </summary>
    /// <returns>The circuit breaker failure count measurement, if a connection manager is registered.</returns>
    private IEnumerable<Measurement<long>> ObserveCircuitBreakerFailureCount()
    {
        var circuitBreaker = ResolveCircuitBreaker();

        if (circuitBreaker is null)
        {
            yield break;
        }

        yield return new Measurement<long>(circuitBreaker.FailureCount);
    }

    /// <summary>
    ///     Resolves the circuit breaker from the registered AMQP connection manager, when available.
    /// </summary>
    /// <returns>The circuit breaker instance, or <see langword="null" /> when AMQP transport is not registered.</returns>
    private AmqpCircuitBreaker? ResolveCircuitBreaker()
    {
        if (_serviceProvider.GetService(typeof(IAmqpConnectionManager)) is AmqpConnectionManager manager)
        {
            return manager.CircuitBreaker;
        }

        return _serviceProvider.GetService(typeof(ITransportCircuitBreaker)) as AmqpCircuitBreaker;
    }
}