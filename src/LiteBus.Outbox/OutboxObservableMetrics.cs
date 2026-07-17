using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Threading;
using LiteBus.Outbox.Abstractions;

namespace LiteBus.Outbox;

/// <summary>
///     Registers observable OpenTelemetry gauges for outbox queue depth and processor state.
/// </summary>
internal sealed class OutboxObservableMetrics : IDisposable
{
    /// <summary>
    ///     The duration cached queue counts remain valid before the next store query.
    /// </summary>
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(10);

    /// <summary>
    ///     Synchronizes access to cached queue counts.
    /// </summary>
    private readonly object _cacheLock = new();

    /// <summary>
    ///     The meter retained for the lifetime of this metrics registrar.
    /// </summary>
    private readonly Meter _meter;

    /// <summary>
    ///     The service provider used to resolve outbox diagnostics dependencies at observation time.
    /// </summary>
    private readonly IServiceProvider _serviceProvider;

    /// <summary>
    ///     Tracks whether the meter has been disposed.
    /// </summary>
    private int _disposeState;

    /// <summary>
    ///     The most recently observed queue counts grouped by status.
    /// </summary>
    private IReadOnlyDictionary<OutboxStatus, int> _cachedCounts = new Dictionary<OutboxStatus, int>();

    /// <summary>
    ///     The UTC timestamp after which cached queue counts should be refreshed.
    /// </summary>
    private DateTimeOffset _cacheExpiresAt;

    /// <summary>
    ///     Initializes a new instance of the <see cref="OutboxObservableMetrics" /> class.
    /// </summary>
    /// <param name="serviceProvider">The service provider used to resolve outbox diagnostics dependencies.</param>
    public OutboxObservableMetrics(IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);

        _serviceProvider = serviceProvider;

        _meter = new Meter(LiteBusOutboxTelemetry.MeterName);

        _meter.CreateObservableGauge(
            LiteBusOutboxTelemetry.QueueDepthInstrumentName,
            ObserveQueueDepth,
            "{message}",
            "Number of outbox messages grouped by status.");

        _meter.CreateObservableGauge(
            LiteBusOutboxTelemetry.ProcessorStateInstrumentName,
            ObserveProcessorState,
            description: "Outbox processor state where 0 is Running, 1 is Paused, and 2 is Draining.");
    }

    /// <summary>
    ///     Disposes the meter and unregisters its observable instruments.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) == 0)
        {
            _meter.Dispose();
        }
    }

    /// <summary>
    ///     Observes outbox queue depth measurements grouped by status.
    /// </summary>
    /// <returns>The current queue depth measurements.</returns>
    private IEnumerable<Measurement<long>> ObserveQueueDepth()
    {
        var counts = GetStatusCounts();

        foreach (var (status, count) in counts)
        {
            yield return new Measurement<long>(
                count,
                new KeyValuePair<string, object?>(LiteBusOutboxTelemetry.QueueStatusAttributeName, status.ToString()));
        }
    }

    /// <summary>
    ///     Observes the current outbox processor state when processor control is registered.
    /// </summary>
    /// <returns>The processor state measurement, if control is available.</returns>
    private IEnumerable<Measurement<int>> ObserveProcessorState()
    {
        var control = _serviceProvider.GetService(typeof(IOutboxProcessorControl)) as IOutboxProcessorControl;

        if (control is null)
        {
            yield break;
        }

        yield return new Measurement<int>((int)control.State);
    }

    /// <summary>
    ///     Returns cached or freshly queried outbox status counts.
    /// </summary>
    /// <returns>A read-only map of status to row count.</returns>
    private IReadOnlyDictionary<OutboxStatus, int> GetStatusCounts()
    {
        lock (_cacheLock)
        {
            if (DateTimeOffset.UtcNow < _cacheExpiresAt)
            {
                return _cachedCounts;
            }
        }

        var store = _serviceProvider.GetService(typeof(IOutboxDiagnosticsStore)) as IOutboxDiagnosticsStore;

        if (store is null)
        {
            return new Dictionary<OutboxStatus, int>();
        }

        try
        {
            var counts = store.GetStatusCountsAsync().ConfigureAwait(false).GetAwaiter().GetResult();

            lock (_cacheLock)
            {
                _cachedCounts = counts;
                _cacheExpiresAt = DateTimeOffset.UtcNow.Add(CacheDuration);
                return _cachedCounts;
            }
        }
#pragma warning disable CA1031 // Status count probes must tolerate any backing-store failure during metric export.
        catch (Exception)
        {
            OutboxDiagnosticsTelemetry.RecordUnavailable();
            return new Dictionary<OutboxStatus, int>();
        }
#pragma warning restore CA1031
    }
}
