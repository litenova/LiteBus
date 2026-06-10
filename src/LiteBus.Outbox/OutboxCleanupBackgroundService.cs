using System;
using System.Diagnostics.Metrics;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Outbox.Abstractions;
using LiteBus.Runtime.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LiteBus.Outbox;

/// <summary>
///     Deletes published outbox messages older than the configured retention period.
/// </summary>
public sealed class OutboxCleanupBackgroundService : IBackgroundService
{
    /// <summary>
    ///     Gets the loop timing and retention options for cleanup.
    /// </summary>
    private readonly OutboxCleanupHostOptions _hostOptions;

    /// <summary>
    ///     Gets the store used to delete published rows.
    /// </summary>
    private readonly IOutboxRetentionStore _stateStore;

    /// <summary>
    ///     Gets the coordinator that records retention cleanup outcomes.
    /// </summary>
    private readonly OutboxRetentionCoordinator _retentionCoordinator;

    /// <summary>
    ///     Gets the clock used to calculate retention cutoffs.
    /// </summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>
    ///     Gets the logger used for cleanup diagnostics.
    /// </summary>
    private readonly ILogger<OutboxCleanupBackgroundService> _logger;

    /// <summary>
    ///     Gets the meter used for cleanup error counters.
    /// </summary>
    private static readonly Meter CleanupMeter = new(LiteBusOutboxTelemetry.MeterName);

    /// <summary>
    ///     Gets the counter incremented when retention cleanup fails.
    /// </summary>
    private static readonly Counter<long> CleanupErrorCounter =
        CleanupMeter.CreateCounter<long>(LiteBusOutboxTelemetry.CleanupErrorInstrumentName);

    /// <summary>
    ///     Initializes a new instance of the <see cref="OutboxCleanupBackgroundService" /> class.
    /// </summary>
    /// <param name="stateStore">The store used to delete published rows.</param>
    /// <param name="hostOptions">The loop timing and retention options for cleanup.</param>
    /// <param name="timeProvider">The clock used to calculate retention cutoffs.</param>
    /// <param name="retentionCoordinator">The coordinator that records retention cleanup outcomes.</param>
    /// <param name="logger">The optional logger for cleanup diagnostics.</param>
    public OutboxCleanupBackgroundService(
        IOutboxRetentionStore stateStore,
        OutboxCleanupHostOptions hostOptions,
        TimeProvider timeProvider,
        OutboxRetentionCoordinator retentionCoordinator,
        ILogger<OutboxCleanupBackgroundService>? logger = null)
    {
        _stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
        _hostOptions = hostOptions ?? throw new ArgumentNullException(nameof(hostOptions));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _retentionCoordinator = retentionCoordinator ?? throw new ArgumentNullException(nameof(retentionCoordinator));
        _logger = logger ?? NullLogger<OutboxCleanupBackgroundService>.Instance;
    }

    /// <inheritdoc />
    public async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_hostOptions.Enabled || _hostOptions.Retention is null || _hostOptions.Retention <= TimeSpan.Zero)
        {
            return;
        }

        var backoff = _hostOptions.Interval > TimeSpan.Zero ? _hostOptions.Interval : TimeSpan.FromSeconds(30);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var runAt = _timeProvider.GetUtcNow();
                var cutoff = runAt - _hostOptions.Retention.Value;
                var deleted = await _stateStore.DeletePublishedOlderThanAsync(cutoff, stoppingToken).ConfigureAwait(false);
                _retentionCoordinator.RecordSuccess(deleted, runAt);
                backoff = _hostOptions.Interval > TimeSpan.Zero ? _hostOptions.Interval : TimeSpan.FromSeconds(30);

                if (_hostOptions.Interval > TimeSpan.Zero)
                {
                    await Task.Delay(_hostOptions.Interval, stoppingToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                CleanupErrorCounter.Add(1);
                _retentionCoordinator.RecordFailure(exception.Message, _timeProvider.GetUtcNow());
                OutboxCleanupLogMessages.CleanupFailed(_logger, exception, backoff);
                await Task.Delay(backoff, stoppingToken).ConfigureAwait(false);
                backoff = TimeSpan.FromMilliseconds(Math.Min(backoff.TotalMilliseconds * 2, TimeSpan.FromMinutes(5).TotalMilliseconds));
            }
        }
    }
}
