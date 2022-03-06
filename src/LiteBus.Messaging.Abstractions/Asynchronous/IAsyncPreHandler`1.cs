using System.Threading.Tasks;

namespace LiteBus.Messaging.Abstractions;

public interface IAsyncPreHandler<in TMessage> : IPreHandler<TMessage, Task>
{
    Task IPreHandler<TMessage, Task>.Handle(IHandleContext<TMessage> context)
    {
        return HandleAsync(context);
    }

    Task HandleAsync(IHandleContext<TMessage> context);
}