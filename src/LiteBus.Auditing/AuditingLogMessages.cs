using System;
using Microsoft.Extensions.Logging;

namespace LiteBus.Auditing;

/// <summary>
///     High-performance auditing chain log messages.
/// </summary>
internal static class AuditingLogMessages
{
    /// <summary>
    ///     The delegate that writes the checkpoint cut retried event.
    /// </summary>
    private static readonly Action<ILogger, string, Guid, Exception?> CheckpointCutRetriedMessage =
        LoggerMessage.Define<string, Guid>(
            LogLevel.Debug,
            new EventId(2701, nameof(CheckpointCutRetried)),
            "The audit segment '{Segment}' moved while checkpoint {CheckpointId} was cut; the stored checkpoint stays as a prefix anchor and the cut retries from the newer head.");

    /// <summary>
    ///     The delegate that writes the checkpoint publish failed event.
    /// </summary>
    private static readonly Action<ILogger, Guid, string, long, Exception?> CheckpointPublishFailedMessage =
        LoggerMessage.Define<Guid, string, long>(
            LogLevel.Warning,
            new EventId(2702, nameof(CheckpointPublishFailed)),
            "Publishing checkpoint {CheckpointId} of segment '{Segment}' at sequence {Sequence} failed; it stays pending and the next pass retries it.");

    /// <summary>
    ///     The delegate that writes the due checkpoint cut failed event.
    /// </summary>
    private static readonly Action<ILogger, string, Exception?> DueCheckpointCutFailedMessage =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(2703, nameof(DueCheckpointCutFailed)),
            "Cutting the due checkpoint of segment '{Segment}' failed; the next scan retries it.");

    /// <summary>
    ///     The delegate that writes the checkpoint publish pass failed event.
    /// </summary>
    private static readonly Action<ILogger, Exception?> CheckpointPublishPassFailedMessage =
        LoggerMessage.Define(
            LogLevel.Error,
            new EventId(2704, nameof(CheckpointPublishPassFailed)),
            "The checkpoint publish pass failed; the next pass runs after a backoff delay.");

    /// <summary>
    ///     The delegate that writes the checkpoint publish disabled event.
    /// </summary>
    private static readonly Action<ILogger, Exception?> CheckpointPublishDisabledMessage =
        LoggerMessage.Define(
            LogLevel.Information,
            new EventId(2710, nameof(CheckpointPublishDisabled)),
            "The checkpoint publish and cut loop is disabled in this host; another host publishes pending checkpoints and cuts due ones.");

    /// <summary>
    ///     The delegate that writes the after-commit link failed event.
    /// </summary>
    private static readonly Action<ILogger, string, Exception?> AfterCommitLinkFailedMessage =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(2705, nameof(AfterCommitLinkFailed)),
            "Linking the staged audit records of segment '{Segment}' after commit failed; the records stay staged and the link sweep retries them.");

    /// <summary>
    ///     Logs that a checkpoint cut retries because the segment moved while it was cut.
    /// </summary>
    /// <param name="logger">The logger receiving the event.</param>
    /// <param name="segment">The segment that moved while the checkpoint was cut.</param>
    /// <param name="checkpointId">The identifier of the stored checkpoint kept as a prefix anchor.</param>
    internal static void CheckpointCutRetried(ILogger logger, string segment, Guid checkpointId)
    {
        CheckpointCutRetriedMessage(logger, segment, checkpointId, null);
    }

    /// <summary>
    ///     Logs that publishing a checkpoint failed and it stays pending for the next pass.
    /// </summary>
    /// <param name="logger">The logger receiving the event.</param>
    /// <param name="checkpointId">The identifier of the checkpoint that failed to publish.</param>
    /// <param name="segment">The segment the checkpoint covers.</param>
    /// <param name="sequence">The sequence the checkpoint covers.</param>
    /// <param name="exception">The exception the publisher threw.</param>
    internal static void CheckpointPublishFailed(
        ILogger logger,
        Guid checkpointId,
        string segment,
        long sequence,
        Exception exception)
    {
        CheckpointPublishFailedMessage(logger, checkpointId, segment, sequence, exception);
    }

    /// <summary>
    ///     Logs that cutting a due checkpoint failed and the next scan retries it.
    /// </summary>
    /// <param name="logger">The logger receiving the event.</param>
    /// <param name="segment">The segment whose due checkpoint failed to cut.</param>
    /// <param name="exception">The exception the policy evaluation or the cut threw.</param>
    internal static void DueCheckpointCutFailed(ILogger logger, string segment, Exception exception)
    {
        DueCheckpointCutFailedMessage(logger, segment, exception);
    }

    /// <summary>
    ///     Logs that the checkpoint publish and cut loop is disabled in this host.
    /// </summary>
    /// <param name="logger">The logger receiving the event.</param>
    internal static void CheckpointPublishDisabled(ILogger logger)
    {
        CheckpointPublishDisabledMessage(logger, null);
    }

    /// <summary>
    ///     Logs that a checkpoint publish pass failed and the next pass runs after a backoff delay.
    /// </summary>
    /// <param name="logger">The logger receiving the event.</param>
    /// <param name="exception">The exception the pass threw.</param>
    internal static void CheckpointPublishPassFailed(ILogger logger, Exception exception)
    {
        CheckpointPublishPassFailedMessage(logger, exception);
    }

    /// <summary>
    ///     Logs that linking staged records after commit failed and the link sweep retries them.
    /// </summary>
    /// <param name="logger">The logger receiving the event.</param>
    /// <param name="segment">The segment whose staged records failed to link.</param>
    /// <param name="exception">The exception the linker threw.</param>
    internal static void AfterCommitLinkFailed(ILogger logger, string segment, Exception exception)
    {
        AfterCommitLinkFailedMessage(logger, segment, exception);
    }
}
