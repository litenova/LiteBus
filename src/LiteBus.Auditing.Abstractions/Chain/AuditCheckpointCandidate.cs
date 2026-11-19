using System;
using LiteBus.Messaging.Abstractions.Auditing;

namespace LiteBus.Auditing.Abstractions;

/// <summary>
///     The state of one chain segment that an <see cref="ICheckpointPolicy" /> decides on.
/// </summary>
/// <param name="Segment">The segment.</param>
/// <param name="Sequence">The sequence the segment has reached.</param>
/// <param name="Count">The number of documents in the segment.</param>
/// <param name="LastCheckpointSequence">
///     The sequence of the last checkpoint, or <see langword="null" /> when none was cut.
/// </param>
/// <param name="LastCheckpointAt">When the last checkpoint was cut, or <see langword="null" /> when none was cut.</param>
/// <param name="Now">The current time.</param>
/// <param name="SegmentInfo">
///     The identity and queryable metadata of the segment, or <see langword="null" /> when the caller did not supply
///     it.
/// </param>
public sealed record AuditCheckpointCandidate(
    string Segment,
    long Sequence,
    long Count,
    long? LastCheckpointSequence,
    DateTimeOffset? LastCheckpointAt,
    DateTimeOffset Now,
    AuditSegmentInfo? SegmentInfo = null);
