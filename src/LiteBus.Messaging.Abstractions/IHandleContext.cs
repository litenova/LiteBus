using System;

namespace LiteBus.Messaging.Abstractions;

public interface IHandleContext
{
    IHandleContextData Data { get; }

    object Message { get; }

    object MessageResult { get; }

    Exception Exception { get; }
}

public class HandleContext : IHandleContext
{
    public HandleContext(object message)
    {
        Message = message;
        Data = new HandleContextData();
    }

    protected HandleContext(IHandleContext context)
    {
        Message = context.Message;
        Data = context.Data;
    }

    public IHandleContextData Data { get; }

    public object Message { get; }

    public object MessageResult { get; set; }

    public Exception Exception { get; set; }
}