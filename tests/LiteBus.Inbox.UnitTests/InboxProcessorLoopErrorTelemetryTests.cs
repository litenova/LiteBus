using System.Diagnostics.Metrics;
using LiteBus.Inbox.Abstractions;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Inbox.UnitTests;

/// <summary>
///     Verifies inbox processor background loop error instrumentation.
/// </summary>
public sealed class InboxProcessorLoopErrorTelemetryTests
{
    /// <summary>
    ///     Confirms an unhandled processor exception increments the loop error counter.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_when_processor_throws_should_increment_loop_errors_counter()
    {
        long measurementCount = 0;
        MeterListener? meterListener = null;

        meterListener = new MeterListener
        {
            InstrumentPublished = (instrument, _) =>
            {
                if (instrument.Meter.Name == "LiteBus.Inbox" &&
                    instrument.Name == "litebus.inbox.processor.loop_errors")
                {
                    meterListener!.EnableMeasurementEvents(instrument);
                }
            }
        };

        meterListener.SetMeasurementEventCallback<long>((_, measurement, _, _) =>
        {
            measurementCount += measurement;
        });

        meterListener.Start();

        var service = new InboxProcessorBackgroundService(
            new ThrowingInboxProcessor(),
            new InboxProcessorOptions { BatchSize = 1, LeaseOwner = "loop-error", LeaseDuration = TimeSpan.FromMinutes(1) },
            new InboxProcessorHostOptions { PollInterval = TimeSpan.FromMilliseconds(10), StartupDelay = TimeSpan.Zero },
            new InboxPollingWorkSignal());

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));

        try
        {
            await service.ExecuteAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
        }

        measurementCount.Should().BeGreaterThan(0);
        meterListener.Dispose();
    }

    private sealed class ThrowingInboxProcessor : IInboxProcessor
    {
        public Task<ProcessorPassResult> ProcessPendingAsync(CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Processor failure.");
        }
    }
}
