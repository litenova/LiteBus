using System;
using System.Collections.Generic;
using System.Linq;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.Descriptors;
using LiteBus.Messaging.Internal.Exceptions;
using LiteBus.Messaging.Internal.Extensions;

namespace LiteBus.Messaging.Internal.Mediator
{
    public class MessageContext : IMessageContext
    {
        private readonly Type _messageType;
        private readonly IServiceProvider _serviceProvider;

        public MessageContext(Type messageType, IMessageDescriptor descriptor, IServiceProvider serviceProvider)
        {
            _messageType = messageType;
            _serviceProvider = serviceProvider;

            Handlers = ResolveHandlers(descriptor.Handlers).ToLazyReadOnlyCollection();
            PostHandlers = ResolvePostHandlers(descriptor.PostHandlers).ToLazyReadOnlyCollection();
            PreHandlers = ResolvePreHandlers(descriptor.PreHandlers).ToLazyReadOnlyCollection();
            ErrorHandlers = ResolveErrorHandlers(descriptor.ErrorHandlers).ToLazyReadOnlyCollection();
        }

        public ILazyReadOnlyCollection<IMessageHandler> Handlers { get; }

        public ILazyReadOnlyCollection<IMessagePostHandler> PostHandlers { get; }

        public ILazyReadOnlyCollection<IMessageErrorHandler> ErrorHandlers { get; }

        public ILazyReadOnlyCollection<IMessagePreHandler> PreHandlers { get; }

        private IEnumerable<Lazy<IMessageHandler>> ResolveHandlers(IReadOnlyCollection<IHandlerDescriptor> descriptors)
        {
            foreach (var descriptor in descriptors)
            {
                var handlerType = descriptor.HandlerType;
                
                if (descriptor.IsGeneric)
                {
                    handlerType = handlerType.MakeGenericType(_messageType.GetGenericArguments());
                }
                
                var resolveFunc = new Func<object?>(() => _serviceProvider.GetService(handlerType));

                yield return new Lazy<IMessageHandler>(() =>
                {
                    var handler = resolveFunc();

                    if (handler is null)
                    {
                        throw new NotResolvedException(handlerType);
                    }

                    return handler as IMessageHandler;
                });
            }
        }

        private IEnumerable<Lazy<IMessagePreHandler>> ResolvePreHandlers(IReadOnlyCollection<IPreHandlerDescriptor> descriptors)
        {
            foreach (var descriptor in descriptors.OrderBy(d => d.Order))
            {
                var hookType = descriptor.PreHandlerType;

                var resolveFunc = new Func<object?>(() => _serviceProvider.GetService(hookType));

                yield return new Lazy<IMessagePreHandler>(() =>
                {
                    var preHandler = resolveFunc();

                    if (preHandler is null)
                    {
                        throw new NotResolvedException(hookType);
                    }

                    return preHandler as IMessagePreHandler;
                });
            }
        }
        
        private IEnumerable<Lazy<IMessageErrorHandler>> ResolveErrorHandlers(IReadOnlyCollection<IErrorHandlerDescriptor> descriptors)
        {
            foreach (var descriptor in descriptors.OrderBy(d => d.Order))
            {
                var errorHandlerType = descriptor.ErrorHandlerType;

                var resolveFunc = new Func<object?>(() => _serviceProvider.GetService(errorHandlerType));

                yield return new Lazy<IMessageErrorHandler>(() =>
                {
                    var errorHandler = resolveFunc();

                    if (errorHandler is null)
                    {
                        throw new NotResolvedException(errorHandlerType);
                    }

                    return errorHandler as IMessageErrorHandler;
                });
            }
        }

        private IEnumerable<Lazy<IMessagePostHandler>> ResolvePostHandlers(IReadOnlyCollection<IPostHandlerDescriptor> descriptors)
        {
            foreach (var descriptor in descriptors.OrderBy(d => d.Order))
            {
                var postHandlerType = descriptor.PostHandlerType;

                var resolveFunc = new Func<object?>(() => _serviceProvider.GetService(postHandlerType));

                yield return new Lazy<IMessagePostHandler>(() =>
                {
                    var postHandler = resolveFunc();

                    if (postHandler is null)
                    {
                        throw new NotResolvedException(postHandlerType);
                    }

                    return postHandler as IMessagePostHandler;
                });
            }
        }
    }
}