using System.Collections.Generic;

namespace LiteBus.Commands.Abstractions;

/// <summary>
///     Reports how a command batch ended and how each of its commands ended.
/// </summary>
public sealed record CommandBatchResult
{
    /// <summary>
    ///     Gets how the batch ended.
    /// </summary>
    public required CommandBatchOutcome Outcome { get; init; }

    /// <summary>
    ///     Gets one entry per command, in the order the commands were sent.
    /// </summary>
    public required IReadOnlyList<CommandBatchEntryResult> Entries { get; init; }

    /// <summary>
    ///     Gets the index of the command that stopped the batch.
    /// </summary>
    /// <value>The index, or <see langword="null" /> when no command stopped the batch.</value>
    public int? FailedIndex { get; init; }

    /// <summary>
    ///     Gets a value indicating whether the batch committed.
    /// </summary>
    public bool IsCommitted => Outcome == CommandBatchOutcome.Committed;
}
