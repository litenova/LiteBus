using System.Threading;

namespace LiteBus.Messaging.Abstractions
{
    public interface ICancellableMessage<out TMessage>
    {
        TMessage Message { get; }

        CancellationToken CancellationToken { get; }
    }

}