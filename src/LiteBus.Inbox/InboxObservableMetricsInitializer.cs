using System;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Runtime.Abstractions;

namespace LiteBus.Inbox;

/// <summary>
///     Ensures <see cref="InboxObservableMetrics" /> is constructed during host startup so observable gauges are active.
/// </summary>
internal sealed class InboxObservableMetricsInitializer : IStartupTask
{
    /// <summary>
    ///     Gets the metrics cache primed during host startup.
    /// </summary>
    private readonly InboxObservableMetrics _metrics;

    /// <summary>
    ///     Initializes a new instance of the <see cref="InboxObservableMetricsInitializer" /> class.
    /// </summary>
    /// <param name="metrics">The inbox observable metrics instance to activate.</param>
    public InboxObservableMetricsInitializer(InboxObservableMetrics metrics)
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
