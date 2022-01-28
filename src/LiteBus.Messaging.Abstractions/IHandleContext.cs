using System;
using System.Threading;

namespace LiteBus.Messaging.Abstractions;

public interface IHandleContext
{
    IHandleContextData Data { get; }

    CancellationToken CancellationToken { get; }

    object Message { get; }

    object MessageResult { get; }

    Exception Exception { get; }
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

    public object MessageResult { get; set; }

    public Exception Exception { get; set; }
}