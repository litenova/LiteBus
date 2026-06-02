using System;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Runtime.Abstractions;
using Microsoft.Extensions.Hosting;

namespace LiteBus.Runtime.Extensions.Microsoft.Hosting;

/// <summary>
///     Adapts <see cref="IBackgroundService" /> to the generic host <see cref="BackgroundService" /> contract.
/// </summary>
internal sealed class BackgroundServiceHostAdapter : BackgroundService
{
    /// <summary>
    ///     The background service executed by the host.
    /// </summary>
    private readonly IBackgroundService _backgroundService;

    /// <summary>
    ///     The gate that blocks continuous loops until startup tasks finish.
    /// </summary>
    private readonly StartupTaskGate _startupGate;

    /// <summary>
    ///     Initializes a new instance of the <see cref="BackgroundServiceHostAdapter" /> class.
    /// </summary>
    /// <param name="backgroundService">The background service executed by the host.</param>
    /// <param name="startupGate">The gate that blocks until startup tasks finish.</param>
    public BackgroundServiceHostAdapter(IBackgroundService backgroundService, StartupTaskGate startupGate)
    {
        _backgroundService = backgroundService ?? throw new ArgumentNullException(nameof(backgroundService));
        _startupGate = startupGate ?? throw new ArgumentNullException(nameof(startupGate));
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await _startupGate.WaitAsync(stoppingToken).ConfigureAwait(false);
        await _backgroundService.ExecuteAsync(stoppingToken).ConfigureAwait(false);
    }
}
