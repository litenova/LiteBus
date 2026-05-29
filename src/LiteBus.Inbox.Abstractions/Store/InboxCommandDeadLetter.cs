using System;

namespace LiteBus.Inbox.Abstractions;

/// <summary>
///     Represents an inbox command moved aside after processing failures.
/// </summary>
public sealed record InboxCommandDeadLetter
{
    /// <summary>
    ///     Gets the command identifier.
    /// </summary>
    public required Guid CommandId { get; init; }

    /// <summary>
    ///     Gets the reason recorded with the dead-letter state.
    /// </summary>
    public required string Reason { get; init; }
}