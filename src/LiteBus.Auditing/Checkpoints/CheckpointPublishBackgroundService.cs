using System;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Auditing.Engine;
using LiteBus.Runtime.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LiteBus.Auditing.Checkpoints;

/// <summary>
///     Publishes pending checkpoints and cuts due ones until the host stops.
/// </summary>
/// <remarks>
///     <para>
///         The chain stores a cut checkpoint as pending and never publishes inside an append: this loop publishes from
///         the background with backoff until every publisher receives it, then cuts a checkpoint for every segment
///         whose policy reports one due, so a quiet segment still gets its checkpoint. A failed publish stays pending
///         with its attempt count and last error recorded on the checkpoint, and verification reports it as an
///         unpublished anchor.
///     </para>
///     <para>
///         When the auditing chain is off, the lifecycle is not registered and the loop exits without doing anything.
///     </para>
/// </remarks>
public sealed class CheckpointPublishBackgroundService : IBackgroundService
{
    /// <summary>
    ///     Resolves the checkpoint lifecycle the passes run on. Held for the life of the service.
    /// </summary>
    private readonly IServiceProvider _serviceProvider;

    /// <summary>
    ///     The clock that measures poll and backoff delays. Held for the life of the service.
    /// </summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>
    ///     The logger failed passes are reported to. Held for the life of the service.
    /// </summary>
    private readonly ILogger<CheckpointPublishBackgroundService> _logger;

    /// <summary>
    ///     How often passes run and how failed passes back off. Held for the life of the service.
    /// </summary>
    private readonly CheckpointPublishHostOptions _options;

    /// <summary>
    ///     Initializes a new instance of the <see cref="CheckpointPublishBackgroundService" /> class.
    /// </summary>
    /// <param name="serviceProvider">Resolves the checkpoint lifecycle the passes run on.</param>
    /// <param name="timeProvider">The clock that measures delays, or <see langword="null" /> for the system clock.</param>
    /// <param name="logger">The logger failed passes are reported to, or <see langword="null" /> for none.</param>
    /// <param name="options">How often passes run, or <see langword="null" /> for the defaults.</param>
    public CheckpointPublishBackgroundService(
        IServiceProvider serviceProvider,
        TimeProvider? timeProvider = null,
        ILogger<CheckpointPublishBackgroundService>? logger = null,
        CheckpointPublishHostOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);

        options?.Validate();

        _serviceProvider = serviceProvider;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _logger = logger ?? NullLogger<CheckpointPublishBackgroundService>.Instance;
        _options = options ?? new CheckpointPublishHostOptions();
    }

    /// <inheritdoc />
    public async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            AuditingLogMessages.CheckpointPublishDisabled(_logger);
            return;
        }

        var lifecycle = (ICheckpointLifecycle?) _serviceProvider.GetService(typeof(ICheckpointLifecycle));

        if (lifecycle is null)
        {
            return;
        }

        var retryDelay = _options.InitialRetryDelay;

        while (!stoppingToken.IsCancellationRequested)
        {
#pragma warning disable CA1031 // A publish pass must survive any store or publisher failure and retry with backoff; the error is logged with context.
            try
            {
                await lifecycle.PublishPendingAsync(stoppingToken).ConfigureAwait(false);
                await lifecycle.CutDueAsync(stoppingToken).ConfigureAwait(false);

                retryDelay = _options.InitialRetryDelay;

                await Task.Delay(_options.PollInterval, _timeProvider, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                AuditingLogMessages.CheckpointPublishPassFailed(_logger, exception);

                try
                {
                    await Task.Delay(retryDelay, _timeProvider, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }

                retryDelay = TimeSpan.FromTicks(Math.Min(retryDelay.Ticks * 2, _options.MaxRetryDelay.Ticks));
            }
#pragma warning restore CA1031
        }
    }
}
