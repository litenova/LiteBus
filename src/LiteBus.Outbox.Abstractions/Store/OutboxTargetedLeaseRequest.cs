using System;
using System.Collections.Generic;

namespace LiteBus.Outbox.Abstractions;

/// <summary>
///     Defines a request to lease specific outbox messages by identifier.
/// </summary>
public sealed record OutboxTargetedLeaseRequest
{
    /// <summary>
    ///     Gets the identifiers of the messages to lease.
    /// </summary>
    public required IReadOnlyList<Guid> MessageIds { get; init; }

    /// <summary>
    ///     Gets the lease owner identifier.
    /// </summary>
    public required string LeaseOwner { get; init; }

    /// <summary>
    ///     Gets the UTC timestamp used by the store for lease checks.
    /// </summary>
    public required DateTimeOffset Now { get; init; }

    /// <summary>
    ///     Gets the duration of the publication lease.
    /// </summary>
    public required TimeSpan LeaseDuration { get; init; }

    /// <summary>
    ///     Gets the optional tenant identifier used to isolate leased messages, with the same meaning as
    ///     <see cref="OutboxLeaseRequest.TenantId" />.
    /// </summary>
    public string? TenantId { get; init; }
}
