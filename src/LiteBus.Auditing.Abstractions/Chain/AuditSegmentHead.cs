using System;

namespace LiteBus.Auditing.Abstractions;

/// <summary>
///     The head of one audit chain segment: its stored identity and the position it has reached.
/// </summary>
/// <param name="Info">The identity and queryable metadata stored on the head.</param>
/// <param name="LastSequence">The sequence of the last linked document, or zero when nothing is linked.</param>
/// <param name="LastHash">The hash of the last linked document, or <see langword="null" /> when nothing is linked.</param>
/// <param name="Count">The number of linked documents in the segment.</param>
/// <param name="LastCheckpointSequence">The sequence the last checkpoint covers, or <see langword="null" /> when none was cut.</param>
/// <param name="LastCheckpointAt">When the last checkpoint was cut, or <see langword="null" /> when none was cut.</param>
/// <param name="Expired">Whether the segment expired and is closed to new writes.</param>
/// <param name="Version">The head version the store checks on a conditional write.</param>
public sealed record AuditSegmentHead(
    AuditSegmentInfo Info,
    long LastSequence,
    string? LastHash,
    long Count,
    long? LastCheckpointSequence,
    DateTimeOffset? LastCheckpointAt,
    bool Expired,
    long Version);
