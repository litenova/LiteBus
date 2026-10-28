using System;

namespace LiteBus.Inbox.Abstractions;

/// <summary>
///     Defines a request to lease inbox messages.
/// </summary>
public sealed record InboxLeaseRequest
{
    /// <summary>
    ///     Gets the maximum number of messages to lease.
    /// </summary>
    public required int BatchSize { get; init; }

    /// <summary>
    ///     Gets the lease owner identifier.
    /// </summary>
    public required string LeaseOwner { get; init; }

    /// <summary>
    ///     Gets the UTC timestamp used by the store for visibility checks.
    /// </summary>
    /// <remarks>
    ///     Stores with a database clock use the database clock for visibility checks; this value is the fallback for
    ///     stores without one.
    /// </remarks>
    public required DateTimeOffset Now { get; init; }

    /// <summary>
    ///     Gets the duration of the processing lease.
    /// </summary>
    public required TimeSpan LeaseDuration { get; init; }

    /// <summary>
    ///     Gets the optional tenant identifier used to isolate leased messages. A <see langword="null" /> value leases across all tenants, while the empty string leases only unscoped rows. Stored and requested tenants compare through <see cref="LiteBus.Messaging.Abstractions.DurableMessaging.DurableTenantId" /> normalization.
    /// </summary>
    public string? TenantId { get; init; }
}
