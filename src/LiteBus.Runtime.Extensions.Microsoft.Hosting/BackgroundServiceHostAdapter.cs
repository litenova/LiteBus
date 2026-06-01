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
    ///     Initializes a new instance of the <see cref="BackgroundServiceHostAdapter" /> class.
    /// </summary>
    /// <param name="backgroundService">The background service executed by the host.</param>
    public BackgroundServiceHostAdapter(IBackgroundService backgroundService)
    {
        _backgroundService = backgroundService ?? throw new ArgumentNullException(nameof(backgroundService));
    }

    /// <inheritdoc />
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        return _backgroundService.ExecuteAsync(stoppingToken);
    }
}
