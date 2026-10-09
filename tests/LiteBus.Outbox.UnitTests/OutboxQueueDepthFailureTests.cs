using System.Diagnostics.Metrics;
using System.Reflection;
using LiteBus.Outbox.Abstractions;
using LiteBus.Runtime.Abstractions.Diagnostics;

namespace LiteBus.Outbox.UnitTests;

/// <summary>
///     Verifies outbox queue depth clears after a diagnostics store failure or absence.
/// </summary>
public sealed class OutboxQueueDepthFailureTests
{
    /// <summary>
    ///     Verifies a throwing diagnostics store clears cached counts and records unavailability.
    /// </summary>
    [Fact]
    public async Task RefreshAsync_AfterStoreFailure_ShouldClearQueueDepthAndRecordUnavailable()
    {
        var store = new SwitchableDiagnosticsStore(new Dictionary<OutboxStatus, int>
        {
            [OutboxStatus.Pending] = 111,
        });
        var measurements = new List<MetricMeasurement>();
        using var listener = CreateListener(measurements);
        using var metrics = new OutboxObservableMetrics(new TestServiceProvider(store));

        await metrics.RefreshAsync().ConfigureAwait(false);
        listener.RecordObservableInstruments();

        measurements.Should().Contain(new MetricMeasurement(
            LiteBusOutboxTelemetry.QueueDepthInstrumentName,
            111,
            nameof(OutboxStatus.Pending)));

        measurements.Clear();
        store.ThrowOnNext = true;

        await metrics.RefreshAsync().ConfigureAwait(false);
        listener.RecordObservableInstruments();

        measurements
            .Where(m => m.InstrumentName == LiteBusOutboxTelemetry.QueueDepthInstrumentName)
            .Should().NotContain(m => m.Value == 111);

        GetCachedCounts(metrics).Should().BeEmpty();

        measurements.Should().Contain(m =>
            m.InstrumentName == LiteBusOutboxTelemetry.DiagnosticsUnavailableInstrumentName);
    }

    /// <summary>
    ///     Verifies a missing diagnostics store records unavailability and emits no depth.
    /// </summary>
    [Fact]
    public async Task RefreshAsync_WhenStoreMissing_ShouldRecordUnavailableAndEmitNoDepth()
    {
        var measurements = new List<MetricMeasurement>();
        using var listener = CreateListener(measurements);
        using var metrics = new OutboxObservableMetrics(new MissingStoreServiceProvider());

        await metrics.RefreshAsync().ConfigureAwait(false);
        listener.RecordObservableInstruments();

        measurements
            .Where(m => m.InstrumentName == LiteBusOutboxTelemetry.QueueDepthInstrumentName)
            .Should().BeEmpty();

        GetCachedCounts(metrics).Should().BeEmpty();

        measurements.Should().Contain(m =>
            m.InstrumentName == LiteBusOutboxTelemetry.DiagnosticsUnavailableInstrumentName);
    }

    private static IReadOnlyDictionary<OutboxStatus, int> GetCachedCounts(OutboxObservableMetrics metrics)
    {
        var field = typeof(OutboxObservableMetrics).GetField("_cachedCounts", BindingFlags.NonPublic | BindingFlags.Instance);
        return (IReadOnlyDictionary<OutboxStatus, int>)field!.GetValue(metrics)!;
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

        public TestServiceProvider(IOutboxDiagnosticsStore diagnosticsStore)
        {
            ArgumentNullException.ThrowIfNull(diagnosticsStore);
            _diagnosticsStore = diagnosticsStore;
        }

        public object? GetService(Type serviceType)
        {
            if (serviceType == typeof(IOutboxDiagnosticsStore))
            {
                return _diagnosticsStore;
            }

            return null;
        }
    }

    private sealed class MissingStoreServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType)
        {
            return null;
        }
    }

    private sealed class SwitchableDiagnosticsStore : IOutboxDiagnosticsStore
    {
        private readonly IReadOnlyDictionary<OutboxStatus, int> _counts;

        public SwitchableDiagnosticsStore(IReadOnlyDictionary<OutboxStatus, int> counts)
        {
            ArgumentNullException.ThrowIfNull(counts);
            _counts = counts;
        }

        public bool ThrowOnNext { get; set; }

        public Task<IReadOnlyDictionary<OutboxStatus, int>> GetStatusCountsAsync(
            CancellationToken cancellationToken = default)
        {
            if (ThrowOnNext)
            {
                throw new InvalidOperationException("Diagnostics store failed.");
            }

            return Task.FromResult(_counts);
        }

        public Task<StoreSchemaInfo> GetSchemaInfoAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(StoreSchemaInfo.ForLogicalStore("outbox", 1));
        }
    }
}
