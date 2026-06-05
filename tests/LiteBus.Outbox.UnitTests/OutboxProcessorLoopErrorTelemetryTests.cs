using System.Diagnostics.Metrics;
using LiteBus.Messaging.Abstractions;
using LiteBus.Outbox;
using LiteBus.Outbox.Abstractions;

namespace LiteBus.Outbox.UnitTests;

/// <summary>
///     Verifies outbox processor background loop error instrumentation.
/// </summary>
public sealed class OutboxProcessorLoopErrorTelemetryTests
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
                if (instrument.Meter.Name == "LiteBus.Outbox" &&
                    instrument.Name == "litebus.outbox.processor.loop_errors")
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

        var service = new OutboxProcessorBackgroundService(
            new ThrowingOutboxProcessor(),
            new OutboxProcessorOptions { BatchSize = 1, LeaseOwner = "loop-error", LeaseDuration = TimeSpan.FromMinutes(1) },
            new OutboxProcessorHostOptions { PollInterval = TimeSpan.FromMilliseconds(10), StartupDelay = TimeSpan.Zero },
            new OutboxPollingWorkSignal(),
            new OutboxProcessorControl());

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

    private sealed class ThrowingOutboxProcessor : IOutboxProcessor
    {
        public Task<ProcessorPassResult> ProcessPendingAsync(CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Processor failure.");
        }
    }
}
