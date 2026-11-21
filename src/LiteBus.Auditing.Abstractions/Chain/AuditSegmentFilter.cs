using System;
using System.Collections.Generic;

namespace LiteBus.Auditing.Abstractions;

/// <summary>
///     Selects audit chain segments for listing, expiry, or erasure.
/// </summary>
/// <param name="TenantId">Only segments of this tenant, or <see langword="null" /> for every tenant.</param>
/// <param name="Attributes">
///     Only segments carrying these attribute values, or <see langword="null" /> for any attributes. An empty
///     collection constrains nothing.
/// </param>
/// <param name="OpenedBefore">Only segments opened before this instant, or <see langword="null" /> for any age.</param>
/// <param name="IncludeExpired">Whether segments that expired are included.</param>
public sealed record AuditSegmentFilter(
    string? TenantId = null,
    IReadOnlyDictionary<string, string>? Attributes = null,
    DateTimeOffset? OpenedBefore = null,
    bool IncludeExpired = false);
