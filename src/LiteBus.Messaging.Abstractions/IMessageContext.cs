using System.Collections.Generic;

namespace LiteBus.Messaging.Abstractions
{
    public interface IMessageContext<TMessage, out TMessageResult>
    {
        TMessage Message { get; }

        IReadOnlyCollection<IMessageHandler<TMessage, TMessageResult>> Handlers { get; }

        IReadOnlyCollection<IPostHandleHook<TMessage>> PostHandleHooks { get; }

        IReadOnlyCollection<IPreHandleHook<TMessage>> PreHandleHooks { get; }
    }
}