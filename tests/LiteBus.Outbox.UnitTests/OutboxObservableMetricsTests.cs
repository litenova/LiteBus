using System.Diagnostics.Metrics;
using LiteBus.Outbox.Abstractions;
using LiteBus.Runtime.Abstractions.Diagnostics;

namespace LiteBus.Outbox.UnitTests;

/// <summary>
///     Verifies observable outbox queue and processor metrics.
/// </summary>
public sealed class OutboxObservableMetricsTests
{
    /// <summary>
    ///     Verifies gauges report cached store counts and processor state until the registrar is disposed.
    /// </summary>
    [Fact]
    public void ObservableGauges_ShouldReportStateCacheCountsAndStopAfterDisposal()
    {
        var diagnosticsStore = new TestDiagnosticsStore(new Dictionary<OutboxStatus, int>
        {
            [OutboxStatus.Pending] = 3,
            [OutboxStatus.DeadLettered] = 2
        });
        var measurements = new List<MetricMeasurement>();
        using var listener = CreateListener(measurements);
        var metrics = new OutboxObservableMetrics(new TestServiceProvider(
            diagnosticsStore,
            new TestProcessorControl(ProcessorState.Draining)));

        listener.RecordObservableInstruments();
        listener.RecordObservableInstruments();

        measurements.Should().Contain(new MetricMeasurement(
            LiteBusOutboxTelemetry.QueueDepthInstrumentName,
            3,
            nameof(OutboxStatus.Pending)));
        measurements.Should().Contain(new MetricMeasurement(
            LiteBusOutboxTelemetry.QueueDepthInstrumentName,
            2,
            nameof(OutboxStatus.DeadLettered)));
        measurements.Should().Contain(new MetricMeasurement(
            LiteBusOutboxTelemetry.ProcessorStateInstrumentName,
            (int)ProcessorState.Draining,
            null));
        diagnosticsStore.QueryCount.Should().Be(1);

        metrics.Dispose();
        metrics.Dispose();
        measurements.Clear();
        listener.RecordObservableInstruments();
        measurements.Should().BeEmpty();
    }

    private static MeterListener CreateListener(ICollection<MetricMeasurement> measurements)
    {
        var listener = new MeterListener
        {
            InstrumentPublished = (instrument, meterListener) =>
            {
                if (instrument.Meter.Name == LiteBusOutboxTelemetry.MeterName)
                {
                    meterListener.EnableMeasurementEvents(instrument);
                }
            }
        };

        listener.SetMeasurementEventCallback<int>((instrument, value, tags, _) =>
            measurements.Add(new MetricMeasurement(instrument.Name, value, FindStatus(tags))));
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
            measurements.Add(new MetricMeasurement(instrument.Name, value, FindStatus(tags))));
        listener.Start();
        return listener;
    }

    private static string? FindStatus(ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        foreach (var tag in tags)
        {
            if (tag.Key == LiteBusOutboxTelemetry.QueueStatusAttributeName)
            {
                return tag.Value as string;
            }
        }

        return null;
    }

    private sealed record MetricMeasurement(string InstrumentName, long Value, string? Status);

    private sealed class TestServiceProvider : IServiceProvider
    {
        private readonly IOutboxDiagnosticsStore _diagnosticsStore;
        private readonly IOutboxProcessorControl _processorControl;

        public TestServiceProvider(
            IOutboxDiagnosticsStore diagnosticsStore,
            IOutboxProcessorControl processorControl)
        {
            _diagnosticsStore = diagnosticsStore;
            _processorControl = processorControl;
        }

        public object? GetService(Type serviceType)
        {
            if (serviceType == typeof(IOutboxDiagnosticsStore))
            {
                return _diagnosticsStore;
            }

            return serviceType == typeof(IOutboxProcessorControl) ? _processorControl : null;
        }
    }

    private sealed class TestDiagnosticsStore : IOutboxDiagnosticsStore
    {
        private readonly IReadOnlyDictionary<OutboxStatus, int> _counts;

        public TestDiagnosticsStore(IReadOnlyDictionary<OutboxStatus, int> counts)
        {
            _counts = counts;
        }

        public int QueryCount { get; private set; }

        public Task<IReadOnlyDictionary<OutboxStatus, int>> GetStatusCountsAsync(
            CancellationToken cancellationToken = default)
        {
            QueryCount++;
            return Task.FromResult(_counts);
        }

        public Task<StoreSchemaInfo> GetSchemaInfoAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(StoreSchemaInfo.ForLogicalStore("outbox", 1));
        }
    }

    private sealed class TestProcessorControl : IOutboxProcessorControl
    {
        public TestProcessorControl(ProcessorState state)
        {
            State = state;
        }

        public ProcessorState State { get; }

        public Task PauseAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task ResumeAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task DrainAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }
}
