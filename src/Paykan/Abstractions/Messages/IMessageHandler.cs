using System.Threading;

namespace Paykan.Abstractions
{
    public interface IMessageHandler<in TMessage, out TResult> where TMessage : IMessage<TResult>
    {
        TResult HandleAsync(TMessage input, CancellationToken cancellation = default);
    }
}