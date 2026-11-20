using System.Collections.Generic;

namespace LiteBus.Auditing.Abstractions;

/// <summary>
///     The state of one audit chain segment as listing reports it.
/// </summary>
/// <param name="Key">The stable persisted segment key.</param>
/// <param name="TenantId">The tenant the segment belongs to, or <see langword="null" /> when the segment is not tenant-scoped.</param>
/// <param name="Attributes">The attributes such as retention class and year.</param>
/// <param name="Count">The number of linked documents in the segment.</param>
/// <param name="LastSequence">The sequence of the last linked document, or <see langword="null" /> when nothing is linked.</param>
/// <param name="LastCheckpointSequence">The sequence the last checkpoint covers, or <see langword="null" /> when none was cut.</param>
/// <param name="Expired">Whether the segment expired and is closed to new writes.</param>
public sealed record AuditSegmentSummary(
    string Key,
    string? TenantId,
    IReadOnlyDictionary<string, string> Attributes,
    long Count,
    long? LastSequence,
    long? LastCheckpointSequence,
    bool Expired);
