using System;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Runtime.Abstractions;
using Microsoft.Extensions.Hosting;

namespace LiteBus.Runtime.Extensions.Hosting;

/// <summary>
///     Adapts a single manifest <see cref="IBackgroundService" /> to <see cref="IHostedService" /> for manual test
///     execution without running the full host orchestrator.
/// </summary>
internal sealed class BackgroundServiceHostWrapper : IHostedService
{
    /// <summary>
    ///     The background service executed by this wrapper.
    /// </summary>
    private readonly IBackgroundService _backgroundService;

    /// <summary>
    ///     The task executing the background service loop.
    /// </summary>
    private Task? _executeTask;

    /// <summary>
    ///     The cancellation source linked to host shutdown for the background service loop.
    /// </summary>
    private CancellationTokenSource? _stoppingCts;

    /// <summary>
    ///     Initializes a new instance of the <see cref="BackgroundServiceHostWrapper" /> class.
    /// </summary>
    /// <param name="backgroundService">The background service executed by the wrapper.</param>
    public BackgroundServiceHostWrapper(IBackgroundService backgroundService)
    {
        ArgumentNullException.ThrowIfNull(backgroundService);

        _backgroundService = backgroundService;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _stoppingCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _executeTask = _backgroundService.ExecuteAsync(_stoppingCts.Token);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_stoppingCts is not null)
        {
            await _stoppingCts.CancelAsync().ConfigureAwait(false);
        }

        if (_executeTask is not null)
        {
            await _executeTask.ConfigureAwait(false);
        }
    }
}
