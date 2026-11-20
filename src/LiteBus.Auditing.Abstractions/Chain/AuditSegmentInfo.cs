using System;
using System.Collections.Generic;

namespace LiteBus.Auditing.Abstractions;

/// <summary>
///     The identity and queryable metadata of an audit chain segment.
/// </summary>
/// <param name="Key">The stable persisted segment key.</param>
/// <param name="TenantId">The tenant the segment belongs to, or <see langword="null" /> when the segment is not tenant-scoped.</param>
/// <param name="Attributes">The attributes such as retention class and year.</param>
/// <param name="OpenedAt">When the segment was opened.</param>
/// <remarks>
///     The key stays the durable chain identity; the tenant, attributes, and opened-at are stored on the head so
///     listing, expiry, and erasure can select segments without parsing keys.
/// </remarks>
public sealed record AuditSegmentInfo(
    string Key,
    string? TenantId,
    IReadOnlyDictionary<string, string> Attributes,
    DateTimeOffset OpenedAt);
