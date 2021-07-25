using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Messaging.Internal
{
    internal class MessageMediator : IMessageMediator
    {
        private readonly IMessageRegistry _messageRegistry;

        public MessageMediator(IMessageRegistry messageRegistry)
        {
            _messageRegistry = messageRegistry;
        }

        public TMessageResult Mediate<TMessage, TMessageResult>(TMessage message, IMediationStrategy strategy)
        {
            var descriptor = _messageRegistry.GetDescriptor(typeof(TMessage));
        }
    }
}