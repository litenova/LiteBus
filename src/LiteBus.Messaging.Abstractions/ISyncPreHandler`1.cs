namespace LiteBus.Messaging.Abstractions;

public interface ISyncPreHandler<in TMessage> : IPreHandler<TMessage, NoResult>
{
    NoResult IPreHandler<TMessage, NoResult>.Handle(IHandleContext<TMessage> context)
    {
        Handle(context);
        return new NoResult();
    }

    new void Handle(IHandleContext<TMessage> context);
}