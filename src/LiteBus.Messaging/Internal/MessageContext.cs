using System;
using System.Collections.Generic;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Messaging.Internal
{
    public class MessageContext<TMessage, TMessageResult> : IMessageContext<TMessage, TMessageResult>
    {
        public MessageContext(TMessage message, IServiceProvider serviceProvider)
        {
            Message = message;
        }
        
        public TMessage Message { get; }

        public IReadOnlyCollection<IMessageHandler<TMessage, TMessageResult>> Handlers { get; }

        public IReadOnlyCollection<IPostHandleHook<TMessage>> PostHandleHooks { get; }

        public IReadOnlyCollection<IPreHandleHook<TMessage>> PreHandleHooks { get; }
    }
}