using System.Collections.Generic;

namespace LiteBus.Messaging.Abstractions
{
    public interface IMessageContext<TMessage, TMessageResult>
    {
        ILazyReadOnlyCollection<IMessageHandler<TMessage, TMessageResult>> Handlers { get; }

        ILazyReadOnlyCollection<IPostHandleHook<TMessage>> PostHandleHooks { get; }

        ILazyReadOnlyCollection<IPreHandleHook<TMessage>> PreHandleHooks { get; }
    }
}