using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Messaging.Abstractions
{
    public interface IAsyncMessageContext<TMessage> : IMessageContext<TMessage, Task>
    {
        CancellationToken CancellationToken { get; }
    }

    public interface IAsyncMessageContext<TMessage, TMessageResult> : IMessageContext<TMessage, Task<TMessageResult>>
    {
        CancellationToken CancellationToken { get; }
    }
}