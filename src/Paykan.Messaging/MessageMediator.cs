using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Paykan.Messaging.Abstractions;
using Paykan.Messaging.Exceptions;
using Paykan.Messaging.Extensions;
using Paykan.Registry.Abstractions;

namespace Paykan.Messaging
{
    /// <inheritdoc cref="IMessageMediator"/> 
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

        public Task SendAsync<TMessage>(TMessage message, Action<IPublishConfiguration> config)
        {
            
        }

        public TMessageResult Send<TMessage, TMessageResult>(TMessage message, Action<ISendConfiguration> config)
        {
            ISendConfiguration configuration = default;
            
            var messageType = typeof(TMessage);

            var descriptor = _messageRegistry.GetDescriptor<TMessage>();

            if (descriptor.HandlerTypes.Count > 1 && configuration.ThrowExceptionOnFindingMultipleHandler)
            {
                throw new MultipleHandlerFoundException(messageType.Name);
            }

            var handler = _serviceProvider.GetHandler<TMessage, TMessageResult>(descriptor.HandlerTypes.First());

            var result = handler.HandleAsync(message, configuration.CancellationToken);

            if (configuration.ExecutePostHandleHooks && result is Task messageResultAsTask)
            {
                return (TMessageResult)messageResultAsTask;
            }

            return result;
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