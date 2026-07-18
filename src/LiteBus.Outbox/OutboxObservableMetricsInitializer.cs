using System;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Runtime.Abstractions;

namespace LiteBus.Outbox;

/// <summary>
///     Ensures <see cref="OutboxObservableMetrics" /> is constructed during host startup so observable gauges are active.
/// </summary>
internal sealed class OutboxObservableMetricsInitializer : IStartupTask
{
    /// <summary>
    ///     Gets the metrics cache primed during host startup.
    /// </summary>
    private readonly OutboxObservableMetrics _metrics;

    /// <summary>
    ///     Initializes a new instance of the <see cref="OutboxObservableMetricsInitializer" /> class.
    /// </summary>
    /// <param name="metrics">The outbox observable metrics instance to activate.</param>
    public OutboxObservableMetricsInitializer(OutboxObservableMetrics metrics)
    {
        ArgumentNullException.ThrowIfNull(metrics);
        _metrics = metrics;
    }

    /// <inheritdoc />
    public Task RunAsync(CancellationToken cancellationToken = default)
    {
        return _metrics.RefreshAsync(cancellationToken);
    }
}
