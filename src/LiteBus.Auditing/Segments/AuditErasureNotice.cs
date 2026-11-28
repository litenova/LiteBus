using System;

namespace LiteBus.Auditing.Segments;

/// <summary>
///     The report published when an erasure deletes audit chain segments.
/// </summary>
/// <param name="Segment">The key of the erased segment.</param>
/// <param name="Count">The number of documents the segment held when it was erased.</param>
/// <param name="Reason">Why the segment was erased.</param>
/// <param name="ErasedAt">When the erasure deleted the segment.</param>
/// <remarks>
///     The notice carries no hash: erasure must remove everything that identifies the subject, including anchors that
///     describe the data, so a notice that carried a hash would anchor exactly what must go.
/// </remarks>
public sealed record AuditErasureNotice(
    string Segment,
    long Count,
    string Reason,
    DateTimeOffset ErasedAt);
