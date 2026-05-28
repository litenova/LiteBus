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
}