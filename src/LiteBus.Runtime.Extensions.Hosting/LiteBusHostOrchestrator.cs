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
internal sealed class LiteBusHostOrchestrator : BackgroundService
{
    /// <summary>
    ///     Gets the host lifetime used to stop the application after an unexpected background service fault.
    /// </summary>
    private readonly IHostApplicationLifetime? _applicationLifetime;

    /// <summary>
    ///     The background services started only after startup tasks complete successfully.
    /// </summary>
    private readonly IReadOnlyList<IBackgroundService> _backgroundServices;

    /// <summary>
    ///     The startup tasks executed during host start before any background service loop begins.
    /// </summary>
    private readonly IReadOnlyList<IStartupTask> _startupTasks;

    /// <summary>
    ///     Initializes a new instance of the <see cref="LiteBusHostOrchestrator" /> class.
    /// </summary>
    /// <param name="startupTasks">The startup tasks to run in registration order.</param>
    /// <param name="backgroundServices">The background services to start after startup tasks succeed.</param>
    /// <param name="applicationLifetime">
    ///     The optional host lifetime used to request fail-closed shutdown when a background service faults.
    /// </param>
    public LiteBusHostOrchestrator(
        IReadOnlyList<IStartupTask> startupTasks,
        IReadOnlyList<IBackgroundService> backgroundServices,
        IHostApplicationLifetime? applicationLifetime = null)
    {
        ArgumentNullException.ThrowIfNull(startupTasks);
        ArgumentNullException.ThrowIfNull(backgroundServices);

        _startupTasks = startupTasks;
        _backgroundServices = backgroundServices;
        _applicationLifetime = applicationLifetime;
    }

    /// <inheritdoc />
    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        foreach (var startupTask in _startupTasks)
        {
            await startupTask.RunAsync(cancellationToken).ConfigureAwait(false);
        }

        await base.StartAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var backgroundTasks = _backgroundServices
            .Select(service => ExecuteBackgroundServiceAsync(service, stoppingToken))
            .ToArray();

        await Task.WhenAll(backgroundTasks).ConfigureAwait(false);
    }

    /// <summary>
    ///     Executes one background service loop, suppresses expected shutdown cancellation, and stops the host on a
    ///     fault.
    /// </summary>
    /// <param name="backgroundService">The background service to execute.</param>
    /// <param name="stoppingToken">A token that signals host shutdown.</param>
    /// <returns>A task that completes when the background service loop exits.</returns>
    private async Task ExecuteBackgroundServiceAsync(
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

#pragma warning disable CA1031 // This host boundary must stop the application for every unexpected loop failure.
        catch (Exception)
        {
            _applicationLifetime?.StopApplication();
            throw;
        }
#pragma warning restore CA1031
    }
}
