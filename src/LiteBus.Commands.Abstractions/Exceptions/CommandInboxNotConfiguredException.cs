using System;

namespace LiteBus.Commands.Abstractions;

/// <summary>
///     Thrown when a command is marked for inbox storage but no command inbox is registered.
/// </summary>
[Serializable]
public sealed class CommandInboxNotConfiguredException : Exception
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="CommandInboxNotConfiguredException" /> class.
    /// </summary>
    /// <param name="commandType">The command type marked with <see cref="StoreInInboxAttribute" />.</param>
    public CommandInboxNotConfiguredException(Type commandType)
        : base($"Command type '{commandType.FullName ?? commandType.Name}' is marked with StoreInInboxAttribute, but no ICommandInbox service is registered. Register ICommandInbox, ICommandInboxProcessor, and CommandInboxProcessorHostedService before sending inbox commands.")
    {
        CommandType = commandType;
    }

    /// <summary>
    ///     Gets the command type that requires an inbox registration.
    /// </summary>
    public Type CommandType { get; }
}