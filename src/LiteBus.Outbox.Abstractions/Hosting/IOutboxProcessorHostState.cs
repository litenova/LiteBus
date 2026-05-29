using System;

namespace LiteBus.Outbox.Abstractions;

/// <summary>
///     Exposes runtime state from the outbox processor host for health checks and operations tooling.
/// </summary>
public interface IOutboxProcessorHostState
{
    /// <summary>
    ///     Gets the UTC timestamp of the last pass that completed without a host-level failure.
    /// </summary>
    DateTimeOffset? LastSuccessfulPassAt { get; }

    /// <summary>
    ///     Gets the UTC timestamp of the last host-level failure.
    /// </summary>
    DateTimeOffset? LastFailureAt { get; }

    /// <summary>
    ///     Gets the message from the last host-level failure.
    /// </summary>
    string? LastFailureMessage { get; }

    /// <summary>
    ///     Gets the number of consecutive host-level failures since the last successful pass.
    /// </summary>
    int ConsecutiveFailures { get; }
}
