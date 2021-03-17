using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.Extensions;
using LiteBus.Registry.Abstractions;
using LiteBus.Messaging.Exceptions;

namespace LiteBus.Messaging
{
    /// <inheritdoc cref="IMessageMediator" />
    public class MessageMediator : IMessageMediator
    {
        private readonly IMessageRegistry _messageRegistry;
        private readonly IServiceProvider _serviceProvider;

        public MessageMediator(IServiceProvider serviceProvider,
                               IMessageRegistry messageRegistry)
        {
            _serviceProvider = serviceProvider;
            _messageRegistry = messageRegistry;
        }

        public virtual Task SendAsync<TMessage>(TMessage message, CancellationToken cancellationToken = default)
        {
            var messageType = typeof(TMessage);

            var descriptor = _messageRegistry.GetDescriptor<TMessage>();

            if (descriptor.HandlerTypes.Count > 1) throw new MultipleMessageHandlerFoundException(messageType.Name);

            var handlers = _serviceProvider.GetHandlers<TMessage, Task>(descriptor.HandlerTypes);

            return Task.WhenAll(handlers.Select(h => h.HandleAsync(message, cancellationToken)));
        }

        public virtual TMessageResult SendAsync<TMessage, TMessageResult>(TMessage message,
                                                                          CancellationToken cancellationToken =
                                                                              default)
        {
            var messageType = typeof(TMessage);

            var descriptor = _messageRegistry.GetDescriptor<TMessage>();

            if (descriptor.HandlerTypes.Count > 1) throw new MultipleMessageHandlerFoundException(messageType.Name);

            var handler = _serviceProvider.GetHandler<TMessage, TMessageResult>(descriptor.HandlerTypes.First());

            return handler.HandleAsync(message, cancellationToken);
        }

        public virtual TMessageResult SendAsync<TMessageResult>(object message,
                                                                CancellationToken cancellationToken = default)
        {
            var messageType = message.GetType();

            var descriptor = _messageRegistry.GetDescriptor(messageType);

            if (descriptor.HandlerTypes.Count > 1) throw new MultipleMessageHandlerFoundException(messageType.Name);

            return _serviceProvider
                   .GetService(descriptor.HandlerTypes.First())
                   .HandleAsync<TMessageResult>(message, cancellationToken);
        }
    }
}