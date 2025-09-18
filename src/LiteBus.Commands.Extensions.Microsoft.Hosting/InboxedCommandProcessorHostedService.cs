using System;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Commands.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LiteBus.Commands.Extensions.Microsoft.Hosting;

/// <summary>
/// A background service that manages the lifecycle of the registered <see cref="ICommandInboxProcessor"/>.
/// This service is responsible for starting the processor when the application starts and ensuring
/// a graceful shutdown when the application stops.
/// </summary>
public sealed class CommandInboxProcessorHostedService : IHostedService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<CommandInboxProcessorHostedService> _logger;
    private readonly ICommandInboxProcessor _processor;
    private readonly CommandBatchHandler _handler;
    private Task? _executingTask;
    private CancellationTokenSource? _stoppingCts;

    /// <summary>
    /// Initializes a new instance of the <see cref="CommandInboxProcessorHostedService"/>.
    /// </summary>
    /// <param name="serviceProvider">The service provider for creating dependency scopes.</param>
    /// <param name="logger">The logger for recording service activity.</param>
    /// <param name="processor">The registered inbox processor implementation. If null, the service will not run.</param>
    public CommandInboxProcessorHostedService(
        IServiceProvider serviceProvider,
        ILogger<CommandInboxProcessorHostedService> logger,
        ICommandInboxProcessor processor)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(processor);

        _serviceProvider = serviceProvider;
        _logger = logger;
        _processor = processor;

        // The handler delegate is created once and reused for the lifetime of the service.
        _handler = HandleCommandsAsync;
    }

    /// <summary>
    /// Starts the inbox processor as a background task.
    /// </summary>
    /// <param name="cancellationToken">A token that signals the application host is starting.</param>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _stoppingCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        _logger.LogInformation("Command Inbox Processor Hosted Service is starting.");

        // Store the long-running task in a field to be managed during shutdown.
        // The task is intentionally not awaited here to allow the application to start up promptly.
        _executingTask = _processor.RunAsync(_handler, _stoppingCts.Token);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Triggers a graceful shutdown of the inbox processor.
    /// </summary>
    /// <param name="cancellationToken">A token that signals a timeout for the shutdown process.</param>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_executingTask is null)
        {
            return;
        }

        _logger.LogInformation("Command Inbox Processor Hosted Service is stopping.");

        try
        {
            // 1. Signal the long-running task to cancel.
            _stoppingCts?.Cancel();
        }
        finally
        {
            // 2. Await the executing task to complete, ensuring all cleanup logic (e.g., in a 'finally'
            //    block of the processor's RunAsync method) has finished. A timeout is used to
            //    prevent the application from hanging indefinitely during shutdown.
            await Task.WhenAny(_executingTask, Task.Delay(Timeout.Infinite, cancellationToken));
            _stoppingCts?.Dispose();
            _logger.LogInformation("Command Inbox Processor Hosted Service has stopped.");
        }
    }

    /// <summary>
    /// The delegate responsible for executing a batch of commands via the mediator.
    /// </summary>
    private async Task HandleCommandsAsync(ICommandInboxBatch commandBatch, CancellationToken cancellationToken)
    {
        // Create a new dependency injection scope for this batch. This ensures that any
        // scoped services (like a DbContext or a Unit of Work) are correctly managed
        // for the duration of the batch processing.
        using var scope = _serviceProvider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<ICommandMediator>();

        _logger.LogDebug("Processing a batch of {CommandCount} commands from the inbox.", commandBatch.Count);

        foreach (var command in commandBatch)
        {
            // Check for cancellation before processing each command in the batch
            // to ensure a fast shutdown if the application is stopping.
            cancellationToken.ThrowIfCancellationRequested();

            var mediationSettings = new CommandMediationSettings();

            // This flag is crucial to prevent the CommandMediator from re-inboxing
            // a command that is already being processed from the inbox.
            mediationSettings.Items["IsInboxExecution"] = true;

            await mediator.SendAsync(command, mediationSettings, cancellationToken);
        }
    }
}