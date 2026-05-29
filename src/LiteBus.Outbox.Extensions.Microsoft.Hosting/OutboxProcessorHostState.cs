using System;

namespace LiteBus.Outbox.Extensions.Microsoft.Hosting;

/// <summary>
///     Tracks runtime state from the outbox processor background service for health checks.
/// </summary>
public sealed class OutboxProcessorHostState
{
    /// <summary>
    ///     Gets the UTC timestamp of the last pass that completed without a host-level failure.
    /// </summary>
    public DateTimeOffset? LastSuccessfulPassAt { get; private set; }

    /// <summary>
    ///     Gets the UTC timestamp of the last host-level failure.
    /// </summary>
    public DateTimeOffset? LastFailureAt { get; private set; }

    /// <summary>
    ///     Gets the message from the last host-level failure.
    /// </summary>
    public string? LastFailureMessage { get; private set; }

    /// <summary>
    ///     Gets the number of consecutive host-level failures since the last successful pass.
    /// </summary>
    public int ConsecutiveFailures { get; private set; }

    /// <summary>
    ///     Records that a processing pass completed without a host-level failure.
    /// </summary>
    /// <param name="timestamp">The UTC timestamp of the completed pass.</param>
    internal void RecordSuccessfulPass(DateTimeOffset timestamp)
    {
        LastSuccessfulPassAt = timestamp;
        LastFailureAt = null;
        LastFailureMessage = null;
        ConsecutiveFailures = 0;
    }

    /// <summary>
    ///     Records a host-level failure that occurred outside individual message dispatch.
    /// </summary>
    /// <param name="timestamp">The UTC timestamp when the failure was observed.</param>
    /// <param name="message">A short failure message suitable for health checks and operations logs.</param>
    internal void RecordFailure(DateTimeOffset timestamp, string message)
    {
        LastFailureAt = timestamp;
        LastFailureMessage = message;
        ConsecutiveFailures++;
    }
}
