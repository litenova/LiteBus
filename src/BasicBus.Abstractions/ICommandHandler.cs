using System.Threading;
using System.Threading.Tasks;

namespace BasicBus.Abstractions
{
    public interface ICommandHandler<in TCommand> where TCommand: ICommand
    {
        Task HandleAsync(TCommand command, CancellationToken cancellation = default);
    }
    
    public interface ICommandHandler<in TCommand, TCommandResult> where TCommand: ICommand<TCommandResult>
    {
        Task<TCommandResult> HandleAsync(TCommand command, CancellationToken cancellation = default);
    }
}