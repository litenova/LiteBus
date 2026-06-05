using System;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Runtime.Abstractions;

namespace LiteBus.Transport;

/// <summary>
///     Startup task that resolves <see cref="TransportObservableMetrics" /> so observable gauges are registered.
/// </summary>
public sealed class TransportObservableMetricsInitializer : IStartupTask
{
    /// <summary>
    ///     The service provider used to resolve transport metrics.
    /// </summary>
    private readonly IServiceProvider _serviceProvider;

    /// <summary>
    ///     Initializes a new instance of the <see cref="TransportObservableMetricsInitializer" /> class.
    /// </summary>
    /// <param name="serviceProvider">The service provider used to resolve transport metrics.</param>
    public TransportObservableMetricsInitializer(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    }

    /// <inheritdoc />
    public Task RunAsync(CancellationToken cancellationToken = default)
    {
        _ = _serviceProvider.GetService(typeof(TransportObservableMetrics));
        return Task.CompletedTask;
    }
}
