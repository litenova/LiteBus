using System;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Outbox.Abstractions;
using LiteBus.Runtime.Abstractions;

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
    private readonly IOutboxStateStore _stateStore;

    /// <summary>
    ///     Gets the clock used to calculate retention cutoffs.
    /// </summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>
    ///     Initializes a new instance of the <see cref="OutboxCleanupBackgroundService" /> class.
    /// </summary>
    /// <param name="stateStore">The store used to delete published rows.</param>
    /// <param name="hostOptions">The loop timing and retention options for cleanup.</param>
    /// <param name="timeProvider">The clock used to calculate retention cutoffs.</param>
    public OutboxCleanupBackgroundService(
        IOutboxStateStore stateStore,
        OutboxCleanupHostOptions hostOptions,
        TimeProvider timeProvider)
    {
        _stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
        _hostOptions = hostOptions ?? throw new ArgumentNullException(nameof(hostOptions));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    /// <inheritdoc />
    public async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_hostOptions.Enabled || _hostOptions.Retention is null || _hostOptions.Retention <= TimeSpan.Zero)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var cutoff = _timeProvider.GetUtcNow() - _hostOptions.Retention.Value;
                await _stateStore.DeletePublishedOlderThanAsync(cutoff, stoppingToken).ConfigureAwait(false);

                if (_hostOptions.Interval > TimeSpan.Zero)
                {
                    await Task.Delay(_hostOptions.Interval, stoppingToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}
