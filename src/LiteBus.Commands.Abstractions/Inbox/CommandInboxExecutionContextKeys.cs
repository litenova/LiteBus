namespace LiteBus.Commands.Abstractions;

/// <summary>
///     Defines execution context item keys used by the command inbox infrastructure.
/// </summary>
public static class CommandInboxExecutionContextKeys
{
    /// <summary>
    ///     Indicates that a command is already being processed from the command inbox.
    /// </summary>
    public const string IsInboxExecution = "__LiteBus.CommandInbox.IsInboxExecution";
}