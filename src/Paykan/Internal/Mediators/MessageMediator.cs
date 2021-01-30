using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Paykan.Abstractions;
using Paykan.Internal.Exceptions;
using Paykan.Internal.Extensions;
using Paykan.Messaging.Abstractions;
using Paykan.Registry.Abstractions;

namespace Paykan.Internal.Mediators
{
    public class MessageMediator : IMessageMediator
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly IMessageRegistry _messageRegistry;

        public MessageMediator(IServiceProvider serviceProvider,
                               IMessageRegistry messageRegistry)
        {
            _serviceProvider = serviceProvider;
            _messageRegistry = messageRegistry;
        }

        public Task SendAsync<TMessage>(TMessage message, CancellationToken cancellationToken = default)
        {
            var messageType = typeof(TMessage);

            var descriptor = _messageRegistry.GetDescriptor<TMessage>();

            if (descriptor.HandlerTypes.Count > 1) throw new MultipleHandlerFoundException(messageType.Name);

            var handlers = _serviceProvider.GetHandlers<TMessage, Task>(descriptor.HandlerTypes);

            return Task.WhenAll(handlers.Select(h => h.HandleAsync(message, cancellationToken)));
        }

        public TMessageResult SendAsync<TMessage, TMessageResult>(TMessage message,
                                                                  CancellationToken cancellationToken = default)
        {
            var messageType = typeof(TMessage);

            var descriptor = _messageRegistry.GetDescriptor<TMessage>();

            if (descriptor.HandlerTypes.Count > 1) throw new MultipleHandlerFoundException(messageType.Name);

            var handler = _serviceProvider.GetHandler<TMessage, TMessageResult>(descriptor.HandlerTypes.First());

            return handler.HandleAsync(message, cancellationToken);
        }

        public TMessageResult SendAsync<TMessageResult>(object message, CancellationToken cancellationToken = default)
        {
            var messageType = message.GetType();
            
            var descriptor = _messageRegistry.GetDescriptor(message.GetType());

            if (descriptor.HandlerTypes.Count > 1) throw new MultipleHandlerFoundException(messageType.Name);
            
            return _serviceProvider
                   .GetService(descriptor.HandlerTypes.First())
                   .HandleAsync<TMessageResult>(message, cancellationToken);
        }
    }
}