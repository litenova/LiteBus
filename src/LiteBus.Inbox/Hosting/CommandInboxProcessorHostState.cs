using System;
using LiteBus.Inbox.Abstractions;

namespace LiteBus.Inbox.Hosting;

/// <summary>
///     Default mutable implementation of <see cref="ICommandInboxProcessorHostState" />.
/// </summary>
internal sealed class CommandInboxProcessorHostState : ICommandInboxProcessorHostState
{
    /// <inheritdoc />
    public DateTimeOffset? LastSuccessfulPassAt { get; private set; }

    /// <inheritdoc />
    public DateTimeOffset? LastFailureAt { get; private set; }

    /// <inheritdoc />
    public string? LastFailureMessage { get; private set; }

    /// <inheritdoc />
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
    ///     Records a host-level failure that occurred outside individual command handling.
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
