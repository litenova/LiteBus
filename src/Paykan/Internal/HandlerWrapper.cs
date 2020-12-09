using System;
using System.Threading;
using Paykan.Abstractions;

namespace Paykan.Internal
{
    internal abstract class HandlerWrapper
    {
        public abstract object HandleAsync(object message,
                                           object handler,
                                           CancellationToken cancellationToken = default);
    }
    
    internal class GenericHandlerWrapper<TMessage, TMessageResult> : HandlerWrapper where TMessage : IMessage<TMessageResult>
    {
        public override object HandleAsync(object message, 
                                           object handler, 
                                           CancellationToken cancellationToken = default)
        {
            var typeSafeHandler = (IMessageHandler<TMessage, TMessageResult>) handler;
                
            return typeSafeHandler.HandleAsync((TMessage)message, cancellationToken);
        }
    }
}