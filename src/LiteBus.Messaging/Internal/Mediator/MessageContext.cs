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
        private readonly IServiceProvider _serviceProvider;

        public MessageContext(IMessageDescriptor descriptor, IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;

            Handlers = ResolveHandlers(descriptor.Handlers).ToLazyReadOnlyCollection();
            PostHandlers = ResolvePostHandleHooks(descriptor.PostHandlers).ToLazyReadOnlyCollection();
            PreHandlers = ResolvePreHandleHooks(descriptor.PreHandlers).ToLazyReadOnlyCollection();
        }

        public ILazyReadOnlyCollection<IMessageHandler> Handlers { get; }

        public ILazyReadOnlyCollection<IMessagePostHandler> PostHandlers { get; }

        public ILazyReadOnlyCollection<IMessagePreHandler> PreHandlers { get; }

        private IEnumerable<Lazy<IMessageHandler>> ResolveHandlers(IReadOnlyCollection<IHandlerDescriptor> descriptors)
        {
            foreach (var descriptor in descriptors)
            {
                var resolveFunc = new Func<object?>(() => _serviceProvider.GetService(descriptor.HandlerType));

                yield return new Lazy<IMessageHandler>(() =>
                {
                    var handler = resolveFunc();

                    if (handler is null)
                    {
                        throw new NotResolvedException(descriptor.HandlerType);
                    }

                    return handler as IMessageHandler;
                });
            }
        }

        private IEnumerable<Lazy<IMessagePreHandler>> ResolvePreHandleHooks(IReadOnlyCollection<IPreHandlerDescriptor> descriptors)
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

        private IEnumerable<Lazy<IMessagePostHandler>> ResolvePostHandleHooks(IReadOnlyCollection<IPostHandlerDescriptor> descriptors)
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