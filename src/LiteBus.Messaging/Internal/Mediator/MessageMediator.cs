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
                                                                IMessageFindStrategy<TMessage> findStrategy,
                                                                IMediationStrategy<TMessage, TMessageResult>
                                                                    mediationStrategy)
        {
            var descriptor = findStrategy.Find(message, _messageRegistry);

            var context = new MessageContext<TMessage, TMessageResult>(descriptor, _serviceProvider);

            return mediationStrategy.Mediate(message, context);
        }
    }
}