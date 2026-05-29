using System;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Inbox.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace LiteBus.Inbox.Hosting;

/// <summary>
///     Microsoft hosting adapter that runs <see cref="ICommandInboxProcessorHost" /> as an <see cref="IHostedService" />.
/// </summary>
public sealed class CommandInboxProcessorHostedService : IHostedService
{
    private readonly ICommandInboxProcessorHost _host;
    private readonly IServiceProvider _serviceProvider;
    private Task? _executingTask;

    /// <summary>
    ///     Initializes a new instance of the <see cref="CommandInboxProcessorHostedService" /> class.
    /// </summary>
    /// <param name="host">The DI-neutral inbox processor host loop.</param>
    /// <param name="serviceProvider">The application service provider used for startup validation.</param>
    public CommandInboxProcessorHostedService(
        ICommandInboxProcessorHost host,
        IServiceProvider serviceProvider)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        CommandInboxHostingDependencyValidator.Validate(_serviceProvider);
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
