using System;
using Microsoft.Extensions.Logging;

namespace LiteBus.Auditing.Anchors.Timestamping;

/// <summary>
///     High-performance RFC 3161 timestamp log messages.
/// </summary>
internal static class Rfc3161TimestampLogMessages
{
    /// <summary>
    ///     The delegate that writes a checkpoint time-stamped event.
    /// </summary>
    private static readonly Action<ILogger, long, string, string, DateTimeOffset, Exception?> CheckpointTimestampedMessage =
        LoggerMessage.Define<long, string, string, DateTimeOffset>(
            LogLevel.Information,
            new EventId(2601, nameof(CheckpointTimestamped)),
            "Checkpoint {Sequence} of segment '{Segment}' was time-stamped by '{Authority}' at {Timestamp}.");

    /// <summary>
    ///     The delegate that writes a missing timestamp signature event.
    /// </summary>
    private static readonly Action<ILogger, long, string, Exception?> TimestampSignatureMissingMessage =
        LoggerMessage.Define<long, string>(
            LogLevel.Debug,
            new EventId(2602, nameof(TimestampSignatureMissing)),
            "Checkpoint {Sequence} of segment '{Segment}' carries no RFC 3161 timestamp signature; verification reports it as invalid.");

    /// <summary>
    ///     The delegate that writes an unusable timestamp signature event.
    /// </summary>
    private static readonly Action<ILogger, long, string, string, Exception?> TimestampSignatureInvalidMessage =
        LoggerMessage.Define<long, string, string>(
            LogLevel.Debug,
            new EventId(2603, nameof(TimestampSignatureInvalid)),
            "Checkpoint {Sequence} of segment '{Segment}' carries an unusable RFC 3161 timestamp signature: {Reason}.");

    /// <summary>
    ///     The delegate that writes a verified timestamp event.
    /// </summary>
    private static readonly Action<ILogger, long, string, Exception?> CheckpointTimestampVerifiedMessage =
        LoggerMessage.Define<long, string>(
            LogLevel.Debug,
            new EventId(2604, nameof(CheckpointTimestampVerified)),
            "Checkpoint {Sequence} of segment '{Segment}' carries an RFC 3161 timestamp signature that covers its hash.");

    /// <summary>
    ///     Writes a checkpoint time-stamped event.
    /// </summary>
    /// <param name="logger">The logger receiving the event.</param>
    /// <param name="sequence">The sequence the checkpoint covers.</param>
    /// <param name="segment">The segment the checkpoint covers.</param>
    /// <param name="authority">The time-stamping authority that issued the token.</param>
    /// <param name="timestamp">The time the token binds the checkpoint hash to.</param>
    public static void CheckpointTimestamped(
        ILogger logger,
        long sequence,
        string segment,
        string authority,
        DateTimeOffset timestamp)
    {
        CheckpointTimestampedMessage(logger, sequence, segment, authority, timestamp, null);
    }

    /// <summary>
    ///     Writes a missing timestamp signature event.
    /// </summary>
    /// <param name="logger">The logger receiving the event.</param>
    /// <param name="sequence">The sequence the checkpoint covers.</param>
    /// <param name="segment">The segment the checkpoint covers.</param>
    public static void TimestampSignatureMissing(ILogger logger, long sequence, string segment)
    {
        TimestampSignatureMissingMessage(logger, sequence, segment, null);
    }

    /// <summary>
    ///     Writes an unusable timestamp signature event.
    /// </summary>
    /// <param name="logger">The logger receiving the event.</param>
    /// <param name="sequence">The sequence the checkpoint covers.</param>
    /// <param name="segment">The segment the checkpoint covers.</param>
    /// <param name="reason">Why the timestamp signature cannot prove the checkpoint hash.</param>
    public static void TimestampSignatureInvalid(ILogger logger, long sequence, string segment, string reason)
    {
        TimestampSignatureInvalidMessage(logger, sequence, segment, reason, null);
    }

    /// <summary>
    ///     Writes a verified timestamp event.
    /// </summary>
    /// <param name="logger">The logger receiving the event.</param>
    /// <param name="sequence">The sequence the checkpoint covers.</param>
    /// <param name="segment">The segment the checkpoint covers.</param>
    public static void CheckpointTimestampVerified(ILogger logger, long sequence, string segment)
    {
        CheckpointTimestampVerifiedMessage(logger, sequence, segment, null);
    }
}
