using System;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Outbox.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace LiteBus.Outbox.Hosting;

/// <summary>
///     Microsoft hosting adapter that runs <see cref="IOutboxProcessorHost" /> as an <see cref="IHostedService" />.
/// </summary>
public sealed class OutboxProcessorHostedService : IHostedService
{
    private readonly IOutboxProcessorHost _host;
    private readonly IServiceProvider _serviceProvider;
    private Task? _executingTask;

    /// <summary>
    ///     Initializes a new instance of the <see cref="OutboxProcessorHostedService" /> class.
    /// </summary>
    /// <param name="host">The DI-neutral outbox processor host loop.</param>
    /// <param name="serviceProvider">The application service provider used for startup validation.</param>
    public OutboxProcessorHostedService(
        IOutboxProcessorHost host,
        IServiceProvider serviceProvider)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        OutboxHostingDependencyValidator.Validate(_serviceProvider);
        _executingTask = _host.RunAsync(cancellationToken);
        return _executingTask.IsCompleted ? _executingTask : Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_executingTask is null)
        {
            return;
        }

        await _executingTask.ConfigureAwait(false);
    }
}
