using System.Threading.Tasks;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Represents an action that is executed on <typeparamref name="TMessage" /> pre-handle phase
/// </summary>
/// <typeparam name="TMessage">The message type that is handled</typeparam>
public interface IMessagePreHandler<in TMessage> : IMessagePreHandler
{
    Task IMessagePreHandler.PreHandleAsync(IHandleContext context)
    {
        return PreHandleAsync(new HandleContext<TMessage>(context));
    }

    Task PreHandleAsync(IHandleContext<TMessage> context);
}