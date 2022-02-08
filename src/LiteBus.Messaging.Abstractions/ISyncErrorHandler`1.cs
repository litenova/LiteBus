namespace LiteBus.Messaging.Abstractions;

public interface ISyncErrorHandler<in TMessage> : IErrorHandler<TMessage, NoResult>
{
    NoResult IErrorHandler<TMessage, NoResult>.Handle(IHandleContext<TMessage> context)
    {
        Handle(context);
        return new NoResult();
    }

    new void Handle(IHandleContext<TMessage> context);
}