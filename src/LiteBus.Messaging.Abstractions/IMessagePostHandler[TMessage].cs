using System.Threading.Tasks;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Represents an action that is executed on <typeparamref name="TMessage" /> pre-handle phase
/// </summary>
/// <typeparam name="TMessage">The message type</typeparam>
public interface IMessagePostHandler<in TMessage> : IMessagePostHandler
{
    Task IMessagePostHandler.PostHandleAsync(IHandleContext context)
    {
        return PostHandleAsync(new HandleContext<TMessage, object>(context));
    }

    Task PostHandleAsync(IHandleContext<TMessage> context);
}