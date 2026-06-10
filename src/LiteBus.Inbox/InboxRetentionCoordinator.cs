using System;
using LiteBus.Inbox.Abstractions;
using LiteBus.Runtime.Abstractions.Diagnostics;

namespace LiteBus.Inbox;

/// <summary>
///     Tracks inbox retention cleanup outcomes for operator diagnostics.
/// </summary>
public sealed class InboxRetentionCoordinator
{
    /// <summary>
    ///     The lock that serializes updates to retention status fields.
    /// </summary>
    private readonly object _sync = new();

    /// <summary>
    ///     Gets the loop timing and retention options for cleanup.
    /// </summary>
    private readonly InboxCleanupHostOptions _hostOptions;

    /// <summary>
    ///     The UTC timestamp of the last cleanup attempt.
    /// </summary>
    private DateTimeOffset? _lastRunAt;

    /// <summary>
    ///     The number of rows deleted during the last successful pass.
    /// </summary>
    private int _lastDeletedCount;

    /// <summary>
    ///     The message from the last failed pass.
    /// </summary>
    private string? _lastError;

    /// <summary>
    ///     Initializes a new instance of the <see cref="InboxRetentionCoordinator" /> class.
    /// </summary>
    /// <param name="hostOptions">The loop timing and retention options for cleanup.</param>
    public InboxRetentionCoordinator(InboxCleanupHostOptions hostOptions)
    {
        _hostOptions = hostOptions ?? throw new ArgumentNullException(nameof(hostOptions));
    }

    /// <summary>
    ///     Records a successful retention cleanup pass.
    /// </summary>
    /// <param name="deletedCount">The number of rows deleted.</param>
    /// <param name="runAt">The UTC timestamp when the pass completed.</param>
    public void RecordSuccess(int deletedCount, DateTimeOffset runAt)
    {
        lock (_sync)
        {
            _lastRunAt = runAt;
            _lastDeletedCount = deletedCount;
            _lastError = null;
        }
    }

    /// <summary>
    ///     Records a failed retention cleanup pass.
    /// </summary>
    /// <param name="errorMessage">The error summary.</param>
    /// <param name="runAt">The UTC timestamp when the pass failed.</param>
    public void RecordFailure(string errorMessage, DateTimeOffset runAt)
    {
        lock (_sync)
        {
            _lastRunAt = runAt;
            _lastError = errorMessage;
        }
    }

    /// <summary>
    ///     Returns the current retention status snapshot.
    /// </summary>
    /// <returns>The configured policy and most recent cleanup outcome.</returns>
    public RetentionRunStatus GetStatus()
    {
        lock (_sync)
        {
            return new RetentionRunStatus(
                _hostOptions.Enabled,
                _hostOptions.Retention,
                _hostOptions.Interval,
                _lastRunAt,
                _lastDeletedCount,
                _lastError);
        }
    }
}
