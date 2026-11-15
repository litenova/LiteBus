namespace LiteBus.Commands.Abstractions;

/// <summary>
///     Describes how a command batch ended.
/// </summary>
public enum CommandBatchOutcome
{
    /// <summary>
    ///     Every command passed and the unit of work committed once.
    /// </summary>
    Committed = 0,

    /// <summary>
    ///     A command stopped the batch, or the commit failed, and nothing was committed.
    /// </summary>
    RolledBack = 1
}
