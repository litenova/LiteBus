using System;
using System.Diagnostics.Metrics;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Inbox.Abstractions;
using LiteBus.Runtime.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LiteBus.Inbox;

/// <summary>
///     Deletes completed inbox messages older than the configured retention period.
/// </summary>
public sealed class InboxCleanupBackgroundService : IBackgroundService
{
    /// <summary>
    ///     Gets the meter used for cleanup error counters.
    /// </summary>
    private static readonly Meter CleanupMeter = new(LiteBusInboxTelemetry.MeterName);

    /// <summary>
    ///     Gets the counter incremented when retention cleanup fails.
    /// </summary>
    private static readonly Counter<long> CleanupErrorCounter =
        CleanupMeter.CreateCounter<long>(LiteBusInboxTelemetry.CleanupErrorInstrumentName);

    /// <summary>
    ///     Gets the loop timing and retention options for cleanup.
    /// </summary>
    private readonly InboxCleanupHostOptions _hostOptions;

    /// <summary>
    ///     Gets the logger used for cleanup diagnostics.
    /// </summary>
    private readonly ILogger<InboxCleanupBackgroundService> _logger;

    /// <summary>
    ///     Gets the coordinator that records retention cleanup outcomes.
    /// </summary>
    private readonly InboxRetentionCoordinator _retentionCoordinator;

    /// <summary>
    ///     Gets the store used to delete completed rows.
    /// </summary>
    private readonly IInboxRetentionStore _stateStore;

    /// <summary>
    ///     Gets the clock used to calculate retention cutoffs.
    /// </summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>
    ///     Initializes a new instance of the <see cref="InboxCleanupBackgroundService" /> class.
    /// </summary>
    /// <param name="stateStore">The store used to delete completed rows.</param>
    /// <param name="hostOptions">The loop timing and retention options for cleanup.</param>
    /// <param name="timeProvider">The clock used to calculate retention cutoffs.</param>
    /// <param name="retentionCoordinator">The coordinator that records retention cleanup outcomes.</param>
    /// <param name="logger">The optional logger for cleanup diagnostics.</param>
    public InboxCleanupBackgroundService(
        IInboxRetentionStore stateStore,
        InboxCleanupHostOptions hostOptions,
        TimeProvider timeProvider,
        InboxRetentionCoordinator retentionCoordinator,
        ILogger<InboxCleanupBackgroundService>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(stateStore);
        _stateStore = stateStore;
        ArgumentNullException.ThrowIfNull(hostOptions);
        _hostOptions = hostOptions;
        ArgumentNullException.ThrowIfNull(timeProvider);
        _timeProvider = timeProvider;
        ArgumentNullException.ThrowIfNull(retentionCoordinator);
        _retentionCoordinator = retentionCoordinator;
        _logger = logger ?? NullLogger<InboxCleanupBackgroundService>.Instance;
    }

    /// <inheritdoc />
    public async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _hostOptions.Validate();

        if (!_hostOptions.Enabled || _hostOptions.Retention is null)
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
                var deleted = await _stateStore.DeleteCompletedOlderThanAsync(cutoff, stoppingToken).ConfigureAwait(false);
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

#pragma warning disable CA1031 // Retention cleanup must backoff and retry across any store or network failure.
            catch (Exception exception)
            {
                CleanupErrorCounter.Add(1);
                _retentionCoordinator.RecordFailure(exception.Message, _timeProvider.GetUtcNow());
                InboxCleanupLogMessages.CleanupFailed(_logger, exception, backoff);
                await Task.Delay(backoff, stoppingToken).ConfigureAwait(false);
                backoff = TimeSpan.FromMilliseconds(Math.Min(backoff.TotalMilliseconds * 2, TimeSpan.FromMinutes(5).TotalMilliseconds));
            }
#pragma warning restore CA1031
        }
    }
}
