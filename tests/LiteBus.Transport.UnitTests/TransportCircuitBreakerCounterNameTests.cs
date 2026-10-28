using System.Diagnostics.Metrics;

namespace LiteBus.Transport.UnitTests;

/// <summary>
///     Verifies the transport breaker counters are emitted under their public contract names.
/// </summary>
public sealed class TransportCircuitBreakerCounterNameTests
{
    /// <summary>
    ///     Verifies failure and open counters are emitted under the public instrument name constants.
    /// </summary>
    [Fact]
    public void BreakerCounters_ShouldEmitUnderPublicContractNames()
    {
        List<string> observed = [];
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, meterListener) =>
            {
                if (instrument.Meter.Name == LiteBusTransportTelemetry.MeterName)
                {
                    meterListener.EnableMeasurementEvents(instrument);
                }
            }
        };

        listener.SetMeasurementEventCallback<long>((instrument, _, _, _) => observed.Add(instrument.Name));
        listener.Start();

        TransportCircuitBreakerTelemetry.RecordFailureObserved(1);
        TransportCircuitBreakerTelemetry.RecordCircuitOpened();

        observed.Should().Contain(LiteBusTransportTelemetry.CircuitBreakerFailureRecordedInstrumentName);
        observed.Should().Contain(LiteBusTransportTelemetry.CircuitBreakerOpenedInstrumentName);
        LiteBusTransportTelemetry.CircuitBreakerFailureRecordedInstrumentName.Should()
            .Be("litebus.transport.circuit_breaker.failure_recorded");
        LiteBusTransportTelemetry.CircuitBreakerOpenedInstrumentName.Should().Be("litebus.transport.circuit_breaker.opened");
    }
}
