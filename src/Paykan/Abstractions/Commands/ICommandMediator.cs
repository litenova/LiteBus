using System.Threading;
using System.Threading.Tasks;

namespace Paykan.Abstractions
{
    /// <summary>
    /// Mediates commands to the corresponding handlers
    /// </summary>
    public interface ICommandMediator
    {
        Task SendAsync<TCommand>(TCommand command,
                                 CancellationToken cancellationToken = default)
            where TCommand : ICommand;

        Task<TCommandResult> SendAsync<TCommand, TCommandResult>(TCommand command,
                                                                 CancellationToken cancellationToken = default)
            where TCommand : ICommand<TCommandResult>;

        Task<TCommandResult> SendAsync<TCommandResult>(ICommand<TCommandResult> command,
                                                       CancellationToken cancellationToken = default);

    }
}