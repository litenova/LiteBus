namespace LiteBus.Messaging.Abstractions;

public interface ISyncPostHandler<in TMessage, in TMessageResult> : IPostHandler<TMessage, TMessageResult, NoResult>
{
    NoResult IPostHandler<TMessage, TMessageResult, NoResult>.Handle(IHandleContext<TMessage, TMessageResult> context)
    {
        Handle(context);
        return new NoResult();
    }

    new void Handle(IHandleContext<TMessage, TMessageResult> context);
}