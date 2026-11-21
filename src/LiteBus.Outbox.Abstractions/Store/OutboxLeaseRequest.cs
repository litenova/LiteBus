using System;

namespace LiteBus.Outbox.Abstractions;

/// <summary>
///     Defines a request to lease outbox messages.
/// </summary>
public sealed record OutboxLeaseRequest
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
    ///     Gets the UTC timestamp used by the store for lease checks.
    /// </summary>
    public required DateTimeOffset Now { get; init; }

    /// <summary>
    ///     Gets the duration of the publication lease.
    /// </summary>
    public required TimeSpan LeaseDuration { get; init; }

    /// <summary>
    ///     Gets the optional tenant identifier used to isolate leased messages. A <see langword="null" /> value means
    ///     no tenant restriction; an empty or blank string leases only unscoped rows. Stored and requested tenants
    ///     compare through <see cref="LiteBus.Messaging.Abstractions.DurableMessaging.DurableTenantId" /> normalization.
    /// </summary>
    public string? TenantId { get; init; }

    /// <summary>
    ///     Gets the most envelopes of one tenant a single lease call returns, or <see langword="null" /> for no cap.
    /// </summary>
    /// <remarks>
    ///     Unscoped envelopes count as one tenant. With a cap, one busy tenant cannot fill every batch while other
    ///     tenants wait. Envelopes past the cap stay pending and are leased by a later call, in the store's usual order.
    /// </remarks>
    public int? MaxPerTenant { get; init; }
}
