using System.Threading.Tasks;
using Paykan.Messaging.Abstractions;

namespace Paykan.Commands.Abstractions
{
    /// <summary>
    ///     Represents the definition of a handler that handles a command without result
    /// </summary>
    /// <typeparam name="TCommand">The type of command</typeparam>
    public interface ICommandHandler<in TCommand> : IMessageHandler<TCommand, Task> where TCommand : ICommand
    {
    }

    /// <summary>
    ///     Represents the definition of a handler that handles a command with result
    /// </summary>
    /// <typeparam name="TCommand">The type of command to</typeparam>
    /// <typeparam name="TCommandResult">The type of command result</typeparam>
    public interface ICommandHandler<in TCommand, TCommandResult> : IMessageHandler<TCommand, Task<TCommandResult>>
        where TCommand : ICommand<TCommandResult>
    {
    }
}