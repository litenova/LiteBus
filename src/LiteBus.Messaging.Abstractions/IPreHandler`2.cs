namespace LiteBus.Messaging.Abstractions;

public interface IPreHandler<in TMessage, out TOutput> : IPreHandler
{
    object IPreHandler.Handle(IHandleContext context)
    {
        return Handle(new HandleContext<TMessage>(context));
    }

    TOutput Handle(IHandleContext<TMessage> context);
}