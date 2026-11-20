using System.Collections.Generic;

namespace LiteBus.Auditing.Abstractions;

/// <summary>
///     The result of verifying one audit chain segment.
/// </summary>
public sealed record AuditChainVerification
{
    /// <summary>
    ///     Gets the verified segment.
    /// </summary>
    public required string Segment { get; init; }

    /// <summary>
    ///     Gets a value indicating whether the segment has no break.
    /// </summary>
    public bool IsIntact => Breaks.Count == 0;

    /// <summary>
    ///     Gets the number of documents read from the segment.
    /// </summary>
    public required long RecordCount { get; init; }

    /// <summary>
    ///     Gets the sequence of the last document, or <see langword="null" /> for an empty segment.
    /// </summary>
    public long? HeadSequence { get; init; }

    /// <summary>
    ///     Gets the hash of the last document, or <see langword="null" /> for an empty segment.
    /// </summary>
    public string? HeadHash { get; init; }

    /// <summary>
    ///     Gets every problem found, in sequence order.
    /// </summary>
    public required IReadOnlyList<AuditChainBreak> Breaks { get; init; }

    /// <summary>
    ///     Gets the latest checkpoint the segment was compared against, or <see langword="null" /> when none exists.
    /// </summary>
    public AuditCheckpoint? LatestCheckpoint { get; init; }

    /// <summary>
    ///     Gets whether the chain matches the latest checkpoint's hash and count, or <see langword="null" /> when there
    ///     is no checkpoint.
    /// </summary>
    public bool? MatchesLatestCheckpoint { get; init; }

    /// <summary>
    ///     Gets whether the latest checkpoint's signature is valid, or <see langword="null" /> when no signer is
    ///     configured or there is no checkpoint.
    /// </summary>
    public bool? CheckpointSignatureValid { get; init; }

    /// <summary>
    ///     Gets one comparison per checkpoint source the segment was checked against.
    /// </summary>
    /// <remarks>
    ///     A source that could not be reached is reported here with
    ///     <see cref="AuditCheckpointComparison.SourceUnreachable" />, as a warning rather than a break: the
    ///     verification stays intact but the gap is visible, separate from a source that disagrees.
    /// </remarks>
    public IReadOnlyList<AuditCheckpointComparison> Comparisons { get; init; } = [];
}
