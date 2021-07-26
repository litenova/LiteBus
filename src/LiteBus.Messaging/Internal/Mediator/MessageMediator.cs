using System;
using System.Linq;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.Extensions;
using LiteBus.Messaging.Internal.Registry;

namespace LiteBus.Messaging.Internal.Mediator
{
    internal class MessageMediator : IMessageMediator
    {
        private readonly IMessageRegistry _messageRegistry;
        private readonly IServiceProvider _serviceProvider;

        public MessageMediator(IMessageRegistry messageRegistry,
                               IServiceProvider serviceProvider)
        {
            _messageRegistry = messageRegistry;
            _serviceProvider = serviceProvider;
        }

        public TMessageResult Mediate<TMessage, TMessageResult>(TMessage message, 
                                                                IMediationStrategy<TMessage, TMessageResult> strategy)
        {
            var descriptor = _messageRegistry.SingleOrDefault(d => d.MessageType == typeof(TMessage)) ??
                             _messageRegistry.SingleOrDefault(d => d.MessageType.BaseType == typeof(TMessage));

            if (descriptor is null)
            {
                throw new MessageNotRegisteredException(typeof(TMessage));    
            }

            var context = new MessageContext<TMessage, TMessageResult>(descriptor, _serviceProvider);

            return strategy.Mediate(message, context);
        }
    }
}