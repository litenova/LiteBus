using System.Threading;

namespace Paykan.Abstractions
{
    public interface IMessageHandler<in TMessage, out TMessageResult> where TMessage : IMessage<TMessageResult>
    {
        TMessageResult HandleAsync(TMessage input, CancellationToken cancellationToken = default);
    }
}