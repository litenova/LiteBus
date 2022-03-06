using System.Threading.Tasks;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Represents an action that is executed on <typeparamref name="TMessage" /> error phase
/// </summary>
/// <typeparam name="TMessage">The message type that is handled</typeparam>
public interface IAsyncErrorHandler<in TMessage> : IErrorHandler<TMessage, Task>
{
    Task IErrorHandler<TMessage, Task>.Handle(IHandleContext<TMessage> context)
    {
        return HandleAsync(context);
    }

    Task HandleAsync(IHandleContext<TMessage> context);
}