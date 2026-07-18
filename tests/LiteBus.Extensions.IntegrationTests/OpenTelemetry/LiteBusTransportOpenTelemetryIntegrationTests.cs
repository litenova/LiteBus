using System.Diagnostics;
using System.Diagnostics.Metrics;
using LiteBus.Transport;
using LiteBus.Transport.Extensions.OpenTelemetry;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace LiteBus.Extensions.IntegrationTests.OpenTelemetry;

/// <summary>
///     Verifies OpenTelemetry provider registration for the shared transport source and meter.
/// </summary>
public sealed class LiteBusTransportOpenTelemetryIntegrationTests
{
    /// <summary>
    ///     Verifies the tracer extension subscribes only to the public transport activity source.
    /// </summary>
    [Fact]
    public void AddLiteBusTransportInstrumentation_ShouldSubscribePublicActivitySourceName()
    {
        using var provider = Sdk.CreateTracerProviderBuilder()
            .AddLiteBusTransportInstrumentation()
            .Build();

        using var subscribedSource = new ActivitySource(LiteBusTransportTelemetry.ActivitySourceName);
        using var activity = subscribedSource.StartActivity("smoke");
        activity.Should().NotBeNull();

        using var unrelatedSource = new ActivitySource("Unrelated.Transport.Source");
        using var ignoredActivity = unrelatedSource.StartActivity("smoke");
        ignoredActivity.Should().BeNull();
    }

    /// <summary>
    ///     Verifies the metrics extension subscribes to the public transport meter.
    /// </summary>
    [Fact]
    public void AddLiteBusTransportMetrics_ShouldSubscribePublicMeterName()
    {
        var observedMeterNames = new List<string>();

        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, meterListener) =>
            {
                if (instrument.Meter.Name == LiteBusTransportTelemetry.MeterName)
                {
                    observedMeterNames.Add(instrument.Meter.Name);
                    meterListener.EnableMeasurementEvents(instrument);
                }
            }
        };

        listener.SetMeasurementEventCallback<long>(static (_, _, _, _) =>
        {
        });
        listener.Start();

        using var provider = Sdk.CreateMeterProviderBuilder()
            .AddLiteBusTransportMetrics()
            .Build();
        using var meter = new Meter(LiteBusTransportTelemetry.MeterName);
        meter.CreateCounter<long>("litebus.transport.test").Add(1);

        provider.ForceFlush();

        observedMeterNames.Should().Contain(LiteBusTransportTelemetry.MeterName);
    }
}
