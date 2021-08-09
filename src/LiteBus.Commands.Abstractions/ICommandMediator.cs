using System.Threading;
using MorseCode.ITask;

namespace LiteBus.Commands.Abstractions
{
    /// <summary>
    ///     Sends commands to their corresponding handlers
    /// </summary>
    public interface ICommandMediator
    {
        /// <summary>
        ///     Sends a command without result to its corresponding handler 
        /// </summary>
        /// <param name="command">the command to send</param>
        /// <param name="cancellationToken">cancellation token</param>
        /// <returns></returns>
        ITask SendAsync<TCommand>(TCommand command, CancellationToken cancellationToken = default)
            where TCommand : ICommand;

        /// <summary>
        ///     Sends a command to its corresponding handler and returns the command result 
        /// </summary>
        /// <param name="command">the command to send</param>
        /// <param name="cancellationToken">cancellation token</param>
        /// <returns>The command result</returns>        
        ITask<TCommandResult> SendAsync<TCommandResult>(ICommand<TCommandResult> command,
                                                       CancellationToken cancellationToken = default);
    }
}