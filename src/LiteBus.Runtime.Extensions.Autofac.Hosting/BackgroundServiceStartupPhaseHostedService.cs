using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Runtime.Abstractions;
using Microsoft.Extensions.Hosting;

namespace LiteBus.Runtime.Extensions.Autofac.Hosting;

/// <summary>
///     Runs startup-phase background services sequentially before signaling long-running loops to start.
/// </summary>
internal sealed class BackgroundServiceStartupPhaseHostedService : IHostedService
{
    /// <summary>
    ///     The startup-phase background services executed during host start.
    /// </summary>
    private readonly IReadOnlyList<IBackgroundServiceStartupInitializer> _startupServices;

    /// <summary>
    ///     The gate released after startup-phase work completes.
    /// </summary>
    private readonly BackgroundServiceStartupGate _gate;

    /// <summary>
    ///     Initializes a new instance of the <see cref="BackgroundServiceStartupPhaseHostedService" /> class.
    /// </summary>
    /// <param name="startupServices">The startup-phase background services to run in registration order.</param>
    /// <param name="gate">The gate that releases continuous background services after startup work completes.</param>
    public BackgroundServiceStartupPhaseHostedService(
        IReadOnlyList<IBackgroundServiceStartupInitializer> startupServices,
        BackgroundServiceStartupGate gate)
    {
        _startupServices = startupServices ?? throw new ArgumentNullException(nameof(startupServices));
        _gate = gate ?? throw new ArgumentNullException(nameof(gate));
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        foreach (var startupService in _startupServices)
        {
            await startupService.ExecuteAsync(cancellationToken).ConfigureAwait(false);
        }

        _gate.SignalComplete();
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
