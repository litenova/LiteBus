using System.Diagnostics.Metrics;
using System.Reflection;
using LiteBus.Inbox.Abstractions;
using LiteBus.Runtime.Abstractions.Diagnostics;

namespace LiteBus.Inbox.UnitTests;

/// <summary>
///     Verifies inbox queue depth clears after a diagnostics store failure.
/// </summary>
public sealed class InboxQueueDepthFailureTests
{
    /// <summary>
    ///     Verifies a throwing diagnostics store clears cached counts and records unavailability.
    /// </summary>
    [Fact]
    public async Task RefreshAsync_AfterStoreFailure_ShouldClearQueueDepthAndRecordUnavailable()
    {
        var store = new SwitchableDiagnosticsStore(new Dictionary<InboxStatus, int>
        {
            [InboxStatus.Pending] = 111,
        });
        var measurements = new List<MetricMeasurement>();
        using var listener = CreateListener(measurements);
        using var metrics = new InboxObservableMetrics(new TestServiceProvider(store));

        await metrics.RefreshAsync().ConfigureAwait(false);
        listener.RecordObservableInstruments();

        measurements.Should().Contain(new MetricMeasurement(
            LiteBusInboxTelemetry.QueueDepthInstrumentName,
            111,
            nameof(InboxStatus.Pending)));

        measurements.Clear();
        store.ThrowOnNext = true;

        await metrics.RefreshAsync().ConfigureAwait(false);
        listener.RecordObservableInstruments();

        measurements
            .Where(m => m.InstrumentName == LiteBusInboxTelemetry.QueueDepthInstrumentName)
            .Should().NotContain(m => m.Value == 111);

        GetCachedCounts(metrics).Should().BeEmpty();

        measurements.Should().Contain(m =>
            m.InstrumentName == LiteBusInboxTelemetry.DiagnosticsUnavailableInstrumentName);
    }

    private static IReadOnlyDictionary<InboxStatus, int> GetCachedCounts(InboxObservableMetrics metrics)
    {
        var field = typeof(InboxObservableMetrics).GetField("_cachedCounts", BindingFlags.NonPublic | BindingFlags.Instance);
        return (IReadOnlyDictionary<InboxStatus, int>) field!.GetValue(metrics)!;
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

        public TestServiceProvider(IInboxDiagnosticsStore diagnosticsStore)
        {
            ArgumentNullException.ThrowIfNull(diagnosticsStore);
            _diagnosticsStore = diagnosticsStore;
        }

        public object? GetService(Type serviceType)
        {
            if (serviceType == typeof(IInboxDiagnosticsStore))
            {
                return _diagnosticsStore;
            }

            return null;
        }
    }

    private sealed class SwitchableDiagnosticsStore : IInboxDiagnosticsStore
    {
        private readonly IReadOnlyDictionary<InboxStatus, int> _counts;

        public SwitchableDiagnosticsStore(IReadOnlyDictionary<InboxStatus, int> counts)
        {
            ArgumentNullException.ThrowIfNull(counts);
            _counts = counts;
        }

        public bool ThrowOnNext { get; set; }

        public Task<IReadOnlyDictionary<InboxStatus, int>> GetStatusCountsAsync(
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
            return Task.FromResult(StoreSchemaInfo.ForLogicalStore("inbox", 1));
        }
    }
}
