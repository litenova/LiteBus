using System.Diagnostics.Metrics;
using LiteBus.Inbox.Abstractions;
using LiteBus.Runtime.Abstractions.Diagnostics;

namespace LiteBus.Inbox.UnitTests;

/// <summary>
///     Verifies observable inbox queue and processor metrics.
/// </summary>
public sealed class InboxObservableMetricsTests
{
    /// <summary>
    ///     Verifies gauges report cached store counts and processor state until the registrar is disposed.
    /// </summary>
    [Fact]
    public void ObservableGauges_ShouldReportStateCacheCountsAndStopAfterDisposal()
    {
        var diagnosticsStore = new TestDiagnosticsStore(new Dictionary<InboxStatus, int>
        {
            [InboxStatus.Pending] = 3,
            [InboxStatus.DeadLettered] = 2
        });
        var measurements = new List<MetricMeasurement>();
        using var listener = CreateListener(measurements);
        var metrics = new InboxObservableMetrics(new TestServiceProvider(
            diagnosticsStore,
            new TestProcessorControl(ProcessorState.Paused)));

        listener.RecordObservableInstruments();
        listener.RecordObservableInstruments();

        measurements.Should().Contain(new MetricMeasurement(
            LiteBusInboxTelemetry.QueueDepthInstrumentName,
            3,
            nameof(InboxStatus.Pending)));
        measurements.Should().Contain(new MetricMeasurement(
            LiteBusInboxTelemetry.QueueDepthInstrumentName,
            2,
            nameof(InboxStatus.DeadLettered)));
        measurements.Should().Contain(new MetricMeasurement(
            LiteBusInboxTelemetry.ProcessorStateInstrumentName,
            (int)ProcessorState.Paused,
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
                if (instrument.Meter.Name == LiteBusInboxTelemetry.MeterName)
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
            if (tag.Key == LiteBusInboxTelemetry.QueueStatusAttributeName)
            {
                return tag.Value as string;
            }
        }

        return null;
    }

    private sealed record MetricMeasurement(string InstrumentName, long Value, string? Status);

    private sealed class TestServiceProvider : IServiceProvider
    {
        private readonly IInboxDiagnosticsStore _diagnosticsStore;
        private readonly IInboxProcessorControl _processorControl;

        public TestServiceProvider(
            IInboxDiagnosticsStore diagnosticsStore,
            IInboxProcessorControl processorControl)
        {
            _diagnosticsStore = diagnosticsStore;
            _processorControl = processorControl;
        }

        public object? GetService(Type serviceType)
        {
            if (serviceType == typeof(IInboxDiagnosticsStore))
            {
                return _diagnosticsStore;
            }

            return serviceType == typeof(IInboxProcessorControl) ? _processorControl : null;
        }
    }

    private sealed class TestDiagnosticsStore : IInboxDiagnosticsStore
    {
        private readonly IReadOnlyDictionary<InboxStatus, int> _counts;

        public TestDiagnosticsStore(IReadOnlyDictionary<InboxStatus, int> counts)
        {
            _counts = counts;
        }

        public int QueryCount { get; private set; }

        public Task<IReadOnlyDictionary<InboxStatus, int>> GetStatusCountsAsync(
            CancellationToken cancellationToken = default)
        {
            QueryCount++;
            return Task.FromResult(_counts);
        }

        public Task<StoreSchemaInfo> GetSchemaInfoAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(StoreSchemaInfo.ForLogicalStore("inbox", 1));
        }
    }

    private sealed class TestProcessorControl : IInboxProcessorControl
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
