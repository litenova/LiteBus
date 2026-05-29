using System;

namespace LiteBus.Outbox.Abstractions;

/// <summary>
///     Represents an outbox message moved aside after publication failures.
/// </summary>
public sealed record OutboxMessageDeadLetter
{
    /// <summary>
    ///     Gets the message identifier.
    /// </summary>
    public required Guid MessageId { get; init; }

    /// <summary>
    ///     Gets the reason recorded with the dead-letter state.
    /// </summary>
    public required string Reason { get; init; }
}