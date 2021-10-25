using System;
using System.Threading;

namespace LiteBus.Messaging.Abstractions
{
    public interface IHandleContext
    {
        IHandleContextData Data { get; }

        CancellationToken CancellationToken { get; }

        object Message { get; }

        object MessageResult { get; }

        public Exception Exception { get;  }
    }

    public interface IHandleContext<out TMessage> : IHandleContext
    {
        object IHandleContext.Message => Message;

        new TMessage Message { get; }
    }

    public interface IHandleContext<out TMessage, out TMessageResult> : IHandleContext<TMessage>
    {
        object IHandleContext.MessageResult => MessageResult;

        new TMessageResult MessageResult { get; }
    }

    public class HandleContext : IHandleContext
    {
        protected HandleContext()
        {
            
        }
        
        public HandleContext(object message, CancellationToken cancellationToken)
        {
            Message = message;
            CancellationToken = cancellationToken;
        }

        public IHandleContextData Data { get; protected set; } = new HandleContextData();

        public CancellationToken CancellationToken { get; protected set; }

        public object Message { get; protected set; }

        public object MessageResult { get; set; }

        public Exception Exception { get; protected set; }

        public void SetException(Exception exception)
        {
            Exception = exception;
        }
    }

    public class HandleContext<TMessage> : HandleContext, IHandleContext<TMessage>
    {
        public HandleContext(IHandleContext context)
        {
            Data = context.Data;
            CancellationToken = context.CancellationToken;
            Message = (TMessage)context.Message;
            MessageResult = context.MessageResult;
            Exception = context.Exception;
        }

        public new TMessage Message { get; }
    }

    public class HandleContext<TMessage, TMessageResult> : HandleContext<TMessage>,
                                                           IHandleContext<TMessage, TMessageResult>
    {
        public new TMessageResult MessageResult { get; set; }

        public HandleContext(IHandleContext context) : base(context)
        {
            MessageResult = (TMessageResult)context.MessageResult;
        }
    }
}