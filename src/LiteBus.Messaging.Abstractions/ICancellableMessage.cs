using System.Threading;

namespace LiteBus.Messaging.Abstractions
{
    public interface ICancellableMessage<out TMessage>
    {
        TMessage Message { get; }

        CancellationToken CancellationToken { get; }
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