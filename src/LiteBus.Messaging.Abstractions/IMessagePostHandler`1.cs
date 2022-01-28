using System.Threading.Tasks;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Represents an action that is executed on <typeparamref name="TMessage" /> pre-handle phase
/// </summary>
/// <typeparam name="TMessage">The message type</typeparam>
/// <typeparam name="TMessageResult">The message result type</typeparam>
public interface IMessagePostHandler<in TMessage, in TMessageResult> : IMessagePostHandler
{
    Task IMessagePostHandler.PostHandleAsync(IHandleContext context)
    {
        return PostHandleAsync(new HandleContext<TMessage, TMessageResult>(context));
    }

    Task PostHandleAsync(IHandleContext<TMessage, TMessageResult> context);
}