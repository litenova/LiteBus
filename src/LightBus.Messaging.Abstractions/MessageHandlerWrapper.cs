using System.Threading;

namespace LightBus.Messaging.Abstractions
{
    internal abstract class MessageHandlerWrapper
    {
        public abstract object HandleAsync(object message,
                                           object handler,
                                           CancellationToken cancellationToken = default);
    }

    internal class GenericMessageHandlerWrapper<TMessage, TMessageResult> : MessageHandlerWrapper
    {
        public override object HandleAsync(object message,
                                           object handler,
                                           CancellationToken cancellationToken = default)
        {
            var typeSafeHandler = (IMessageHandler<TMessage, TMessageResult>) handler;

            return typeSafeHandler.HandleAsync((TMessage) message, cancellationToken);
        }
    }
}