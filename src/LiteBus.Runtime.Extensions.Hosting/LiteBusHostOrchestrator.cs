using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Runtime.Abstractions;
using Microsoft.Extensions.Hosting;

namespace LiteBus.Runtime.Extensions.Hosting;

/// <summary>
///     Runs manifest startup tasks sequentially, then executes background service loops after all startup tasks succeed.
/// </summary>
internal sealed class LiteBusHostOrchestrator : IHostedService
{
    /// <summary>
    ///     The background services started only after startup tasks complete successfully.
    /// </summary>
    private readonly IReadOnlyList<IBackgroundService> _backgroundServices;

    /// <summary>
    ///     The task executing background service loops after startup succeeds.
    /// </summary>
    private Task? _backgroundExecutionTask;

    /// <summary>
    ///     The startup tasks executed during host start before any background service loop begins.
    /// </summary>
    private readonly IReadOnlyList<IStartupTask> _startupTasks;

    /// <summary>
    ///     The cancellation source linked to host shutdown for background service execution.
    /// </summary>
    private CancellationTokenSource? _stoppingCts;

    /// <summary>
    ///     Initializes a new instance of the <see cref="LiteBusHostOrchestrator" /> class.
    /// </summary>
    /// <param name="startupTasks">The startup tasks to run in registration order.</param>
    /// <param name="backgroundServices">The background services to start after startup tasks succeed.</param>
    public LiteBusHostOrchestrator(
        IReadOnlyList<IStartupTask> startupTasks,
        IReadOnlyList<IBackgroundService> backgroundServices)
    {
        ArgumentNullException.ThrowIfNull(startupTasks);
        ArgumentNullException.ThrowIfNull(backgroundServices);

        _startupTasks = startupTasks;
        _backgroundServices = backgroundServices;
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        foreach (var startupTask in _startupTasks)
        {
            await startupTask.RunAsync(cancellationToken).ConfigureAwait(false);
        }

        if (_backgroundServices.Count == 0)
        {
            return;
        }

        _stoppingCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _backgroundExecutionTask = ExecuteBackgroundServicesAsync(_stoppingCts.Token);
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_stoppingCts is not null)
        {
            await _stoppingCts.CancelAsync().ConfigureAwait(false);
        }

        if (_backgroundExecutionTask is not null)
        {
            await _backgroundExecutionTask.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Runs each background service loop concurrently until host shutdown is requested.
    /// </summary>
    /// <param name="stoppingToken">A token that signals host shutdown.</param>
    /// <returns>A task that completes when every background service loop has exited.</returns>
    private async Task ExecuteBackgroundServicesAsync(CancellationToken stoppingToken)
    {
        var backgroundTasks = _backgroundServices
            .Select(service => ExecuteBackgroundServiceAsync(service, stoppingToken))
            .ToArray();

        await Task.WhenAll(backgroundTasks).ConfigureAwait(false);
    }

    /// <summary>
    ///     Executes one background service loop and suppresses expected cancellation on host shutdown.
    /// </summary>
    /// <param name="backgroundService">The background service to execute.</param>
    /// <param name="stoppingToken">A token that signals host shutdown.</param>
    /// <returns>A task that completes when the background service loop exits.</returns>
    private static async Task ExecuteBackgroundServiceAsync(
        IBackgroundService backgroundService,
        CancellationToken stoppingToken)
    {
        try
        {
            await backgroundService.ExecuteAsync(stoppingToken).ConfigureAwait(false);
        }
        // Expected when host shutdown cancels the background service loop.
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }
}
