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

        public TMessageResult SendAsync<TMessage, TMessageResult>(TMessage message, CancellationToken cancellationToken)
        {
            var descriptor = _messageRegistry.GetDescriptor<TMessage>()
                                             .;

            var handler = _serviceProvider.GetService(descriptor.HandlerTypes.Single()) as IAsyncMessageHandler;
        }

        public Task SendAsync<TMessage>(TMessage message, CancellationToken cancellationToken)
        {
            throw new System.NotImplementedException();
        }

        public IAsyncEnumerable<TMessageResult> StreamAsync<TMessage, TMessageResult>(TMessage message, CancellationToken cancellationToken)
        {
            throw new System.NotImplementedException();
        }

        public TMessageResult Send<TMessage, TMessageResult>(TMessage message)
        {
            throw new System.NotImplementedException();
        }

        public void Send<TMessage>(TMessage message)
        {
            throw new System.NotImplementedException();
        }
    }
}