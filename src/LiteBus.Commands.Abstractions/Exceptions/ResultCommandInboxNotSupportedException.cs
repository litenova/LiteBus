using System;

namespace LiteBus.Commands.Abstractions;

/// <summary>
///     Thrown when a result-returning command is marked for command inbox storage.
/// </summary>
[Serializable]
public sealed class ResultCommandInboxNotSupportedException : Exception
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="ResultCommandInboxNotSupportedException" /> class.
    /// </summary>
    /// <param name="commandType">The result-returning command type marked with <see cref="StoreInInboxAttribute" />.</param>
    public ResultCommandInboxNotSupportedException(Type commandType)
        : base($"Command type '{commandType.FullName ?? commandType.Name}' implements ICommand<TResult> and is marked with StoreInInboxAttribute. The command inbox accepts only ICommand commands because deferred execution cannot return a synchronous business result.")
    {
        CommandType = commandType;
    }

    /// <summary>
    ///     Gets the command type that cannot be stored in the inbox.
    /// </summary>
    public Type CommandType { get; }
}