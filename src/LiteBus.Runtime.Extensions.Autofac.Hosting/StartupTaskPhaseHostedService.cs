using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Runtime.Abstractions;
using Microsoft.Extensions.Hosting;

namespace LiteBus.Runtime.Extensions.Autofac.Hosting;

/// <summary>
///     Runs startup tasks sequentially before signaling long-running background services to start.
/// </summary>
internal sealed class StartupTaskPhaseHostedService : IHostedService
{
    /// <summary>
    ///     The startup tasks executed during host start.
    /// </summary>
    private readonly IReadOnlyList<IStartupTask> _startupTasks;

    /// <summary>
    ///     The gate released after startup tasks complete.
    /// </summary>
    private readonly StartupTaskGate _gate;

    /// <summary>
    ///     Initializes a new instance of the <see cref="StartupTaskPhaseHostedService" /> class.
    /// </summary>
    /// <param name="startupTasks">The startup tasks to run in registration order.</param>
    /// <param name="gate">The gate that releases continuous background services after startup tasks complete.</param>
    public StartupTaskPhaseHostedService(
        IReadOnlyList<IStartupTask> startupTasks,
        StartupTaskGate gate)
    {
        _startupTasks = startupTasks ?? throw new ArgumentNullException(nameof(startupTasks));
        _gate = gate ?? throw new ArgumentNullException(nameof(gate));
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        foreach (var startupTask in _startupTasks)
        {
            await startupTask.RunAsync(cancellationToken).ConfigureAwait(false);
        }

        _gate.SignalComplete();
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
