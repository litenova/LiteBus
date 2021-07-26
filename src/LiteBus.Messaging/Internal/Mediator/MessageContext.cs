using System;
using System.Linq;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Internal.Extensions;

namespace LiteBus.Messaging.Internal.Mediator
{
    public class MessageContext<TMessage, TMessageResult> : IMessageContext<TMessage, TMessageResult>
    {
        public MessageContext(IMessageDescriptor descriptor, IServiceProvider serviceProvider)
        {
            Handlers = descriptor.PostHandleHookDescriptors
                                 .OrderBy(d => d.Order)
                                 .Select(h => new Lazy<IMessageHandler<TMessage, TMessageResult>>(() =>
                                             serviceProvider.GetService(h.HookType) as
                                                 IMessageHandler<TMessage, TMessageResult>))
                                 .ToLazyReadOnlyCollection();
        }

        public ILazyReadOnlyCollection<IMessageHandler<TMessage, TMessageResult>> Handlers { get; }

        public ILazyReadOnlyCollection<IPreHandleHook<TMessage>> PostHandleHooks { get; }

        public ILazyReadOnlyCollection<IPreHandleHook<TMessage>> PreHandleHooks { get; }
    }
}