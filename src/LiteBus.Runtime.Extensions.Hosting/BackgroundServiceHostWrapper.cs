using System;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Runtime.Abstractions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

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
    ///     The logger used for host shutdown diagnostics.
    /// </summary>
    private readonly ILogger<BackgroundServiceHostWrapper> _logger;

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
    /// <param name="logger">The optional logger for host shutdown diagnostics.</param>
    public BackgroundServiceHostWrapper(IBackgroundService backgroundService, ILogger<BackgroundServiceHostWrapper>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(backgroundService);

        _backgroundService = backgroundService;
        _logger = logger ?? NullLogger<BackgroundServiceHostWrapper>.Instance;
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
        try
        {
            if (_stoppingCts is not null)
            {
                await _stoppingCts.CancelAsync().ConfigureAwait(false);
            }

            if (_executeTask is not null)
            {
                try
                {
                    await _executeTask.WaitAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (_stoppingCts?.IsCancellationRequested == true && _executeTask.IsCanceled)
                {
                    // The wrapper asked the loop to stop and the loop ended canceled: normal shutdown.
                }
                catch (OperationCanceledException) when (!_executeTask.IsCompleted)
                {
                    var serviceType = _backgroundService.GetType();
                    BackgroundServiceHostWrapperLogMessages.LoopAbandoned(
                        _logger,
                        serviceType.FullName ?? serviceType.Name);
                }
            }
        }
        finally
        {
            _stoppingCts?.Dispose();
            _stoppingCts = null;
        }
    }
}
