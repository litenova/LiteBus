using System.Threading;
using System.Threading.Tasks;

namespace BasicBus.Abstractions
{
    /// <summary>
    /// Mediates commands to the corresponding handlers
    /// </summary>
    public interface ICommandMediator
    {
        Task SendAsync(ICommand command, CancellationToken cancellationToken = default);

        Task<TCommandResult> SendAsync<TCommandResult>(ICommand<TCommandResult> command,
                                                       CancellationToken cancellationToken = default);

        void Send(ICommand command);

        TCommandResult Send<TCommandResult>(ICommand<TCommandResult> command);
    }
}