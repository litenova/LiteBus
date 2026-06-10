using System.Diagnostics;
using System.Diagnostics.Metrics;
using LiteBus.Outbox;
using LiteBus.Outbox.Extensions.OpenTelemetry;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace LiteBus.Extensions.OpenTelemetry.IntegrationTests;

public sealed class LiteBusOutboxOpenTelemetryIntegrationTests
{
    [Fact]
    public void TelemetryConstants_ShouldExposeStableConsumerContractNames()
    {
        LiteBusOutboxTelemetry.ActivitySourceName.Should().Be("LiteBus.Outbox");
        LiteBusOutboxTelemetry.MeterName.Should().Be("LiteBus.Outbox");
    }

    [Fact]
    public void AddLiteBusOutboxInstrumentation_ShouldSubscribePublicActivitySourceName()
    {
        using var provider = Sdk.CreateTracerProviderBuilder()
            .AddLiteBusOutboxInstrumentation()
            .Build();

        using var subscribedSource = new ActivitySource(LiteBusOutboxTelemetry.ActivitySourceName);
        using var activity = subscribedSource.StartActivity("smoke");
        activity.Should().NotBeNull();

        using var unrelatedSource = new ActivitySource("Unrelated.Source");
        using var ignoredActivity = unrelatedSource.StartActivity("smoke");
        ignoredActivity.Should().BeNull();
    }

    [Fact]
    public void AddLiteBusOutboxMetrics_ShouldSubscribePublicMeterName()
    {
        var observedMeterNames = new List<string>();

        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, meterListener) =>
            {
                if (instrument.Meter.Name == LiteBusOutboxTelemetry.MeterName)
                {
                    observedMeterNames.Add(instrument.Meter.Name);
                    meterListener.EnableMeasurementEvents(instrument);
                }
            }
        };
        listener.SetMeasurementEventCallback<long>((_, _, _, _) => { });
        listener.Start();

        using var provider = Sdk.CreateMeterProviderBuilder()
            .AddLiteBusOutboxMetrics()
            .Build();

        using var meter = new Meter(LiteBusOutboxTelemetry.MeterName);
        meter.CreateCounter<long>(LiteBusOutboxTelemetry.ProcessorLeasesAcquiredInstrumentName).Add(1);

        provider.ForceFlush();

        observedMeterNames.Should().Contain(LiteBusOutboxTelemetry.MeterName);
    }
}
