using System.Threading;

namespace LiteBus.Messaging.Abstractions
{
    public interface ICancellableMessage
    {
        object Message { get; }

        CancellationToken CancellationToken { get; }
    }
    
    public interface ICancellableMessage<out TMessage> : ICancellableMessage
    {
        object ICancellableMessage.Message => Message;

        new TMessage Message { get; }
    }
    
    public class CancellableMessage<TMessage> : ICancellableMessage<TMessage>
    {
        public CancellableMessage(TMessage message, CancellationToken cancellationToken)
        {
            Message = message;
            CancellationToken = cancellationToken;
        }

        public TMessage Message { get; }
        public CancellationToken CancellationToken { get; }
    }
}