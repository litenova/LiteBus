namespace LiteBus.Messaging.Abstractions;

public interface IHandleContext<out TMessage> : IHandleContext where TMessage : notnull
{
    new TMessage Message { get; }

    object IHandleContext.Message => Message;
}

public class HandleContext<TMessage> : HandleContext, IHandleContext<TMessage> where TMessage : notnull
{
    public HandleContext(IHandleContext context) : base(context.Message, context.CancellationToken)
    {
        Data = context.Data;
        MessageResult = context.MessageResult;
        Exception = context.Exception;
    }

    public new TMessage Message => (TMessage) base.Message;
}