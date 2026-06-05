using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using LiteBus.Inbox.Abstractions;

namespace LiteBus.Inbox;

/// <summary>
///     Registers observable OpenTelemetry gauges for inbox queue depth and processor state.
/// </summary>
internal sealed class InboxObservableMetrics
{
    /// <summary>
    ///     The duration cached queue counts remain valid before the next store query.
    /// </summary>
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(10);

    /// <summary>
    ///     The service provider used to resolve inbox diagnostics dependencies at observation time.
    /// </summary>
    private readonly IServiceProvider _serviceProvider;

    /// <summary>
    ///     Synchronizes access to cached queue counts.
    /// </summary>
    private readonly object _cacheLock = new();

    /// <summary>
    ///     The UTC timestamp after which cached queue counts should be refreshed.
    /// </summary>
    private DateTimeOffset _cacheExpiresAt;

    /// <summary>
    ///     The most recently observed queue counts grouped by status.
    /// </summary>
    private IReadOnlyDictionary<InboxStatus, int> _cachedCounts =
        new Dictionary<InboxStatus, int>();

    /// <summary>
    ///     Initializes a new instance of the <see cref="InboxObservableMetrics" /> class.
    /// </summary>
    /// <param name="serviceProvider">The service provider used to resolve inbox diagnostics dependencies.</param>
    public InboxObservableMetrics(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));

        var meter = new Meter(LiteBusInboxTelemetry.MeterName);
        meter.CreateObservableGauge(
            LiteBusInboxTelemetry.QueueDepthInstrumentName,
            ObserveQueueDepth,
            unit: "{message}",
            description: "Number of inbox messages grouped by status.");

        meter.CreateObservableGauge(
            LiteBusInboxTelemetry.ProcessorStateInstrumentName,
            ObserveProcessorState,
            description: "Inbox processor state where 0 is Running, 1 is Paused, and 2 is Draining.");
    }

    /// <summary>
    ///     Observes inbox queue depth measurements grouped by status.
    /// </summary>
    /// <returns>The current queue depth measurements.</returns>
    private IEnumerable<Measurement<long>> ObserveQueueDepth()
    {
        var counts = GetStatusCounts();

        foreach (var (status, count) in counts)
        {
            yield return new Measurement<long>(
                count,
                new KeyValuePair<string, object?>(LiteBusInboxTelemetry.QueueStatusAttributeName, status.ToString()));
        }
    }

    /// <summary>
    ///     Observes the current inbox processor state when processor control is registered.
    /// </summary>
    /// <returns>The processor state measurement, if control is available.</returns>
    private IEnumerable<Measurement<int>> ObserveProcessorState()
    {
        var control = _serviceProvider.GetService(typeof(IInboxProcessorControl)) as IInboxProcessorControl;
        if (control is null)
        {
            yield break;
        }

        yield return new Measurement<int>((int)control.State);
    }

    /// <summary>
    ///     Returns cached or freshly queried inbox status counts.
    /// </summary>
    /// <returns>A read-only map of status to row count.</returns>
    private IReadOnlyDictionary<InboxStatus, int> GetStatusCounts()
    {
        lock (_cacheLock)
        {
            if (DateTimeOffset.UtcNow < _cacheExpiresAt)
            {
                return _cachedCounts;
            }
        }

        var store = _serviceProvider.GetService(typeof(IInboxDiagnosticsStore)) as IInboxDiagnosticsStore;
        if (store is null)
        {
            return new Dictionary<InboxStatus, int>();
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
        catch
        {
            return new Dictionary<InboxStatus, int>();
        }
    }
}
