using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Commands.Abstractions
{
    /// <summary>
    ///     Mediates a command to its corresponding handler
    /// </summary>
    public interface ICommandMediator
    {
        Task SendAsync<TCommand>(TCommand command, CancellationToken cancellationToken = default)
            where TCommand : ICommand;

        Task<TCommandResult> SendAsync<TCommand, TCommandResult>(TCommand command,
                                                                 CancellationToken cancellationToken = default)
            where TCommand : ICommand<TCommandResult>;

        Task<TCommandResult> SendAsync<TCommandResult>(ICommand<TCommandResult> command,
                                                       CancellationToken cancellationToken = default);
    }
}