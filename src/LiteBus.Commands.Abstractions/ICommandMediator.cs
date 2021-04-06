using System.Threading.Tasks;

namespace LiteBus.Commands.Abstractions
{
    /// <summary>
    ///     Mediates a command to its corresponding handler
    /// </summary>
    public interface ICommandMediator
    {
        Task SendAsync(ICommand command);
        Task<TCommandResult> SendAsync<TCommandResult>(ICommand<TCommandResult> command);
    }
}