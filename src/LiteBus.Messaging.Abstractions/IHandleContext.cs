using System;
using System.Threading;

namespace LiteBus.Messaging.Abstractions;

public interface IHandleContext
{
    IHandleContextData Data { get; }

    CancellationToken CancellationToken { get; }

    object Message { get; }

    object? MessageResult { get; }

    Exception? Exception { get; }
}

public interface IHandleContext<out TMessage> : IHandleContext where TMessage : notnull
{
    new TMessage Message { get; }

    object IHandleContext.Message => Message;
}

public interface IHandleContext<out TMessage, out TMessageResult> : IHandleContext<TMessage> where TMessage : notnull
{
    new TMessageResult? MessageResult { get; }

    object? IHandleContext.MessageResult => MessageResult;
}

public class HandleContext : IHandleContext
{
    public HandleContext(object message, CancellationToken cancellationToken)
    {
        Message = message;
        CancellationToken = cancellationToken;
    }

    public IHandleContextData Data { get; protected set; } = new HandleContextData();

    public CancellationToken CancellationToken { get; protected set; }

    public object Message { get; }

    public object? MessageResult { get; set; }

    public Exception? Exception { get; set; }
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

public class HandleContext<TMessage, TMessageResult> : HandleContext<TMessage>,
                                                       IHandleContext<TMessage, TMessageResult>
    where TMessage : notnull
{
    public HandleContext(IHandleContext context) : base(context)
    {
        MessageResult = context.MessageResult is not null ? (TMessageResult) context.MessageResult : default;
    }

    public new TMessageResult? MessageResult { get; }
}