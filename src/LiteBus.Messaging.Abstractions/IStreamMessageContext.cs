using System.Collections.Generic;
using System.Threading;

namespace LiteBus.Messaging.Abstractions
{
    public interface IStreamMessageContext<TMessage, TMessageResult> : IMessageContext<TMessage, IAsyncEnumerable<TMessageResult>>
    {
        CancellationToken CancellationToken { get; }
    }
}