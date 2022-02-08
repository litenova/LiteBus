using System.Threading.Tasks;

namespace LiteBus.Messaging.Abstractions;

public interface IAsyncPostHandler<in TMessage> : IPostHandler<TMessage, Task>
{
    Task IPostHandler<TMessage, Task>.Handle(IHandleContext<TMessage> context)
    {
        return HandleAsync(context);
    }

    Task HandleAsync(IHandleContext<TMessage> context);
}