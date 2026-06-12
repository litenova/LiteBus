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
    ///     Initializes a new instance of the <see cref="InboxObservableMetricsInitializer" /> class.
    /// </summary>
    /// <param name="metrics">The inbox observable metrics instance to activate.</param>
    public InboxObservableMetricsInitializer(InboxObservableMetrics metrics)
    {
        ArgumentNullException.ThrowIfNull(metrics);
    }

    /// <inheritdoc />
    public Task RunAsync(CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}