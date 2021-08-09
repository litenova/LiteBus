using LiteBus.Messaging.Abstractions;

namespace LiteBus.Commands.Abstractions
{
    /// <summary>
    ///     Represents the definition of a handler that handles a command without result
    /// </summary>
    /// <typeparam name="TCommand">The type of command</typeparam>
    public interface ICommandHandler<TCommand> : IAsyncMessageHandler<TCommand> where TCommand : ICommand
    {
    }

    /// <summary>
    ///     Represents the definition of a handler that handles a command with result
    /// </summary>
    /// <typeparam name="TCommand">The type of command to</typeparam>
    /// <typeparam name="TCommandResult">The type of command result</typeparam>
    public interface ICommandHandler<TCommand, TCommandResult> : IAsyncMessageHandler<TCommand, TCommandResult>
        where TCommand : ICommand<TCommandResult>
    {
    }
}