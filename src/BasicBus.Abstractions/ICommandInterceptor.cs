using System.Threading.Tasks;

namespace BasicBus.Abstractions
{
    public interface ICommandInterceptor
    {
        Task OnPreHandleAsync(ICommand command);
        Task OnPostHandleAsync(ICommand command);
    }
    
    public interface ICommandInterceptor<TCommand> where TCommand : ICommand
    {
        Task OnPreHandleAsync(TCommand command);

        Task OnPostHandleAsync(TCommand command);
    }
    
    public interface ICommandInterceptor<TCommand, TCommandResult> where TCommand : ICommand<TCommandResult>
    {
        Task OnPreHandleAsync(TCommand command);

        Task OnPostHandleAsync(TCommand command);
    }
}