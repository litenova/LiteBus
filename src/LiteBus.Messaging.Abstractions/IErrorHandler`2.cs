namespace LiteBus.Messaging.Abstractions;

public interface IErrorHandler<in TMessage, out TOutput> : IErrorHandler
{
    object IErrorHandler.Handle(IHandleContext context)
    {
        return Handle(new HandleContext<TMessage>(context));
    }

    TOutput Handle(IHandleContext<TMessage> context);
}