namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     The generic base of all message handlers
/// </summary>
public interface IHandler<in TMessage, out TMessageResult> : IHandler
{
    object IHandler.Handle(IHandleContext context)
    {
        return Handle(new HandleContext<TMessage>(context));
    }

    TMessageResult Handle(IHandleContext<TMessage> context);
}