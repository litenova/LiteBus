using System;

namespace LiteBus.Inbox.Abstractions;

/// <summary>
///     Represents a failed inbox message attempt.
/// </summary>
public sealed record InboxEnvelopeFailure
{
    /// <summary>
    ///     Gets the message identifier.
    /// </summary>
    public required Guid Id { get; init; }

    /// <summary>
    ///     Gets the latest processing error.
    /// </summary>
    public required string Error { get; init; }

    /// <summary>
    ///     Gets the earliest UTC timestamp for the next attempt.
    /// </summary>
    public DateTimeOffset? VisibleAfter { get; init; }
}
