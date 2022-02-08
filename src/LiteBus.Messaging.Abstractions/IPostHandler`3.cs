namespace LiteBus.Messaging.Abstractions;

public interface IPostHandler<in TMessage, in TMessageResult, out TOutput> : IPostHandler
{
    object IPostHandler.Handle(IHandleContext context)
    {
        return Handle(new HandleContext<TMessage, TMessageResult>(context));
    }

    TOutput Handle(IHandleContext<TMessage, TMessageResult> context);
}