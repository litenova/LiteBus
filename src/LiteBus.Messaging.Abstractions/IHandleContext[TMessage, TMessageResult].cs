namespace LiteBus.Messaging.Abstractions;

public interface IHandleContext<out TMessage, out TMessageResult> : IHandleContext<TMessage> where TMessage : notnull
{
    new TMessageResult MessageResult { get; }

    object IHandleContext.MessageResult => MessageResult;
}

public class HandleContext<TMessage, TMessageResult> : HandleContext<TMessage>,
                                                       IHandleContext<TMessage, TMessageResult>
    where TMessage : notnull
{
    public HandleContext(IHandleContext context) : base(context)
    {
        MessageResult = context.MessageResult is not null ? (TMessageResult) context.MessageResult : default;
    }

    public new TMessageResult MessageResult { get; }
}