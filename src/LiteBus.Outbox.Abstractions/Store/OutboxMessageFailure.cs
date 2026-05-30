using System;

namespace LiteBus.Outbox.Abstractions;

/// <summary>
///     Represents a failed outbox publication attempt.
/// </summary>
public sealed record OutboxEnvelopeFailure
{
    /// <summary>
    ///     Gets the message identifier.
    /// </summary>
    public required Guid Id { get; init; }

    /// <summary>
    ///     Gets the latest publication error.
    /// </summary>
    public required string Error { get; init; }

    /// <summary>
    ///     Gets the earliest UTC timestamp for the next attempt.
    /// </summary>
    public DateTimeOffset? VisibleAfter { get; init; }
}