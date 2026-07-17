using System.Diagnostics.Metrics;

namespace LiteBus.Transport.UnitTests;

/// <summary>
///     Verifies observable transport circuit breaker metrics.
/// </summary>
public sealed class TransportObservableMetricsTests
{
    /// <summary>
    ///     Verifies gauges report breaker state with the registered broker tag and stop after disposal.
    /// </summary>
    [Fact]
    public void ObservableGauges_ShouldReportBreakerStateAndStopAfterDisposal()
    {
        var registry = new TransportCircuitBreakerRegistry(new TransportCircuitBreakerOptions
        {
            FailureThreshold = 1,
            BreakDuration = TimeSpan.FromMinutes(1)
        });
        var breaker = registry.GetPublisherCircuit("orders");
        breaker.RecordFailure(breaker.AcquirePermit());

        var measurements = new List<MetricMeasurement>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, meterListener) =>
            {
                if (instrument.Meter.Name == LiteBusTransportTelemetry.MeterName &&
                    (instrument.Name == LiteBusTransportTelemetry.CircuitBreakerOpenInstrumentName ||
                     instrument.Name == LiteBusTransportTelemetry.CircuitBreakerFailureCountInstrumentName))
                {
                    meterListener.EnableMeasurementEvents(instrument);
                }
            }
        };

        listener.SetMeasurementEventCallback<int>((instrument, value, tags, _) =>
            measurements.Add(new MetricMeasurement(instrument.Name, value, FindBroker(tags))));
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
            measurements.Add(new MetricMeasurement(instrument.Name, value, FindBroker(tags))));
        listener.Start();

        var metrics = new TransportObservableMetrics(new TestServiceProvider(registry, "amqp"));
        listener.RecordObservableInstruments();

        measurements.Should().Contain(new MetricMeasurement(
            LiteBusTransportTelemetry.CircuitBreakerOpenInstrumentName,
            1,
            "amqp"));
        measurements.Should().Contain(new MetricMeasurement(
            LiteBusTransportTelemetry.CircuitBreakerFailureCountInstrumentName,
            1,
            "amqp"));

        metrics.Dispose();
        metrics.Dispose();
        measurements.Clear();
        listener.RecordObservableInstruments();
        measurements.Should().BeEmpty();
    }

    private static string? FindBroker(ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        foreach (var tag in tags)
        {
            if (tag.Key == LiteBusTransportTelemetry.BrokerTagName)
            {
                return tag.Value as string;
            }
        }

        return null;
    }

    private sealed record MetricMeasurement(string InstrumentName, long Value, string? Broker);

    private sealed class TestServiceProvider : IServiceProvider
    {
        private readonly TransportBrokerIdentity _brokerIdentity;
        private readonly ITransportCircuitBreakerRegistry _circuitBreakerRegistry;

        public TestServiceProvider(ITransportCircuitBreakerRegistry circuitBreakerRegistry, string broker)
        {
            _circuitBreakerRegistry = circuitBreakerRegistry;
            _brokerIdentity = new TransportBrokerIdentity(broker);
        }

        public object? GetService(Type serviceType)
        {
            if (serviceType == typeof(ITransportCircuitBreakerRegistry))
            {
                return _circuitBreakerRegistry;
            }

            return serviceType == typeof(TransportBrokerIdentity) ? _brokerIdentity : null;
        }
    }
}
