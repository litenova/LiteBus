using System.Threading;

namespace BasicBus.Abstractions
{
    public interface IMessageHandler<in TMessage, out TResult> where TMessage : IMessage<TResult>
    {
        TResult HandleAsync(TMessage input, CancellationToken cancellation = default);
    }
}