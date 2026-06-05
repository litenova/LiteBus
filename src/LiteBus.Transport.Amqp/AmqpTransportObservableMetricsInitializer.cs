using System.Threading;
using System.Threading.Tasks;
using LiteBus.Runtime.Abstractions;

namespace LiteBus.Transport.Amqp;

/// <summary>
///     Ensures <see cref="AmqpTransportObservableMetrics" /> is constructed during host startup so observable gauges are active.
/// </summary>
internal sealed class AmqpTransportObservableMetricsInitializer : IStartupTask
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="AmqpTransportObservableMetricsInitializer" /> class.
    /// </summary>
    /// <param name="metrics">The AMQP observable metrics instance to activate.</param>
    public AmqpTransportObservableMetricsInitializer(AmqpTransportObservableMetrics metrics)
    {
        _ = metrics ?? throw new System.ArgumentNullException(nameof(metrics));
    }

    /// <inheritdoc />
    public Task RunAsync(CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}
