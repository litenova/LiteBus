using System.Threading.Tasks;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Represents an action that is executed on <typeparamref name="TMessage" /> error phase
/// </summary>
/// <typeparam name="TMessage">The message type that is handled</typeparam>
public interface IMessageErrorHandler<in TMessage> : IMessageErrorHandler
{
    Task IMessageErrorHandler.HandleErrorAsync(IHandleContext context)
    {
        return HandleErrorAsync(new HandleContext<TMessage>(context));
    }

    Task HandleErrorAsync(IHandleContext<TMessage> context);
}