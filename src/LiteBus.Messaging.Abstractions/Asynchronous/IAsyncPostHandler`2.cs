using System.Threading.Tasks;

namespace LiteBus.Messaging.Abstractions;

public interface IAsyncPostHandler<in TMessage, in TMessageResult> : IPostHandler<TMessage, TMessageResult, Task>
{
    Task IPostHandler<TMessage, TMessageResult, Task>.Handle(IHandleContext<TMessage, TMessageResult> context)
    {
        return HandleAsync(context);
    }

    Task HandleAsync(IHandleContext<TMessage, TMessageResult> context);
}