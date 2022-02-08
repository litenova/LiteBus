namespace LiteBus.Messaging.Abstractions;

public interface ISyncPostHandler<in TMessage> : IPostHandler<TMessage, NoResult>
{
    NoResult IPostHandler<TMessage, NoResult>.Handle(IHandleContext<TMessage> context)
    {
        Handle(context);
        return new NoResult();
    }

    new void Handle(IHandleContext<TMessage> context);
}