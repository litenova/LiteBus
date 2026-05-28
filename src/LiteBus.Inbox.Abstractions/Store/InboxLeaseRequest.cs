using System;

namespace LiteBus.Inbox.Abstractions;

/// <summary>
///     Defines a request to lease inbox commands.
/// </summary>
public sealed record InboxLeaseRequest
{
    /// <summary>
    ///     Gets the maximum number of commands to lease.
    /// </summary>
    public required int BatchSize { get; init; }

    /// <summary>
    ///     Gets the lease owner identifier.
    /// </summary>
    public required string LeaseOwner { get; init; }

    /// <summary>
    ///     Gets the UTC timestamp used by the store for visibility checks.
    /// </summary>
    public required DateTimeOffset Now { get; init; }

    /// <summary>
    ///     Gets the duration of the processing lease.
    /// </summary>
    public required TimeSpan LeaseDuration { get; init; }
}