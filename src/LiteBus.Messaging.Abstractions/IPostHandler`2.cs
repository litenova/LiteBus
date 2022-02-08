namespace LiteBus.Messaging.Abstractions;

public interface IPostHandler<in TMessage, out TOutput> : IPostHandler
{
    object IPostHandler.Handle(IHandleContext context)
    {
        return Handle(new HandleContext<TMessage>(context));
    }

    TOutput Handle(IHandleContext<TMessage> context);
}