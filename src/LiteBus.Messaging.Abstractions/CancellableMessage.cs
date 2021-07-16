using System.Threading;

namespace LiteBus.Messaging.Abstractions
{
    public interface ICancellableMessage<TMessage>
    {
        TMessage Message { get; set; }
        CancellationToken CancellationToken { get; set; }
    }

    public class CancellableMessage<TMessage> : ICancellableMessage<TMessage>
    {
        public TMessage Message { get; set; }

        public CancellationToken CancellationToken { get; set; }
    }
}