using System;

namespace LiteBus.Inbox.Abstractions;

/// <summary>
///     Represents an inbox message moved aside after processing failures.
/// </summary>
public sealed record InboxEnvelopeDeadLetter
{
    /// <summary>
    ///     Gets the message identifier.
    /// </summary>
    public required Guid Id { get; init; }

    /// <summary>
    ///     Gets the reason recorded with the dead-letter state.
    /// </summary>
    public required string Reason { get; init; }
}
