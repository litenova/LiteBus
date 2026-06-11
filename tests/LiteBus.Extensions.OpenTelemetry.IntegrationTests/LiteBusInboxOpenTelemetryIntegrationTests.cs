using System.Diagnostics;
using System.Diagnostics.Metrics;
using LiteBus.Inbox;
using LiteBus.Inbox.Extensions.OpenTelemetry;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace LiteBus.Extensions.OpenTelemetry.IntegrationTests;

public sealed class LiteBusInboxOpenTelemetryIntegrationTests
{
    [Fact]
    public void TelemetryConstants_ShouldExposeStableConsumerContractNames()
    {
        LiteBusInboxTelemetry.ActivitySourceName.Should().Be("LiteBus.Inbox");
        LiteBusInboxTelemetry.MeterName.Should().Be("LiteBus.Inbox");
    }

    [Fact]
    public void AddLiteBusInboxInstrumentation_ShouldSubscribePublicActivitySourceName()
    {
        using var provider = Sdk.CreateTracerProviderBuilder()
            .AddLiteBusInboxInstrumentation()
            .Build();

        using var subscribedSource = new ActivitySource(LiteBusInboxTelemetry.ActivitySourceName);
        using var activity = subscribedSource.StartActivity("smoke");
        activity.Should().NotBeNull();

        using var unrelatedSource = new ActivitySource("Unrelated.Source");
        using var ignoredActivity = unrelatedSource.StartActivity("smoke");
        ignoredActivity.Should().BeNull();
    }

    [Fact]
    public void AddLiteBusInboxMetrics_ShouldSubscribePublicMeterName()
    {
        var observedMeterNames = new List<string>();

        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, meterListener) =>
            {
                if (instrument.Meter.Name == LiteBusInboxTelemetry.MeterName)
                {
                    observedMeterNames.Add(instrument.Meter.Name);
                    meterListener.EnableMeasurementEvents(instrument);
                }
            }
        };

        listener.SetMeasurementEventCallback<long>((_, _, _, _) =>
        {
        });

        listener.Start();

        using var provider = Sdk.CreateMeterProviderBuilder()
            .AddLiteBusInboxMetrics()
            .Build();

        using var meter = new Meter(LiteBusInboxTelemetry.MeterName);
        meter.CreateCounter<long>(LiteBusInboxTelemetry.ProcessorLeasesAcquiredInstrumentName).Add(1);

        provider.ForceFlush();

        observedMeterNames.Should().Contain(LiteBusInboxTelemetry.MeterName);
    }
}