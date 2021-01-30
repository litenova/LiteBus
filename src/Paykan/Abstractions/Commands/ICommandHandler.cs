using System.Threading.Tasks;
using Paykan.Messaging.Abstractions;

namespace Paykan.Abstractions
{
    /// <summary>
    /// The handler to handle the commands without result
    /// </summary>
    /// <typeparam name="TCommand">The type of command to be handled</typeparam>
    public interface ICommandHandler<in TCommand> : IMessageHandler<TCommand, Task> where TCommand : ICommand
    {
    }

    /// <summary>
    /// The handler to handle the commands with result
    /// </summary>
    /// <typeparam name="TCommand">The type of command to be handled</typeparam>
    /// <typeparam name="TCommandResult">The type of command result</typeparam>
    public interface ICommandHandler<in TCommand, TCommandResult> : IMessageHandler<TCommand, Task<TCommandResult>>
        where TCommand : ICommand<TCommandResult>
    {
    }
}