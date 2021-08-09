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

            Handlers = ResolveHandlers(descriptor.HandlerDescriptors).ToLazyReadOnlyCollection();
            PostHandleAsyncHooks = ResolvePostHandleHooks(descriptor.PostHandleHookDescriptors).ToLazyReadOnlyCollection();
            PreHandleAsyncHooks = ResolvePreHandleHooks(descriptor.PreHandleHookDescriptors).ToLazyReadOnlyCollection();
        }

        public ILazyReadOnlyCollection<IMessageHandler> Handlers { get; }

        public ILazyReadOnlyCollection<IAsyncHook> PostHandleAsyncHooks { get; }

        public ILazyReadOnlyCollection<IAsyncHook> PreHandleAsyncHooks { get; }

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

        private IEnumerable<Lazy<IAsyncHook>> ResolvePreHandleHooks(IReadOnlyCollection<IHookDescriptor> descriptors)
        {
            foreach (var descriptor in descriptors.OrderBy(d => d.Order))
            {
                var hookType = descriptor.HookType;

                var resolveFunc = new Func<object?>(() => _serviceProvider.GetService(hookType));

                yield return new Lazy<IAsyncHook>(() =>
                {
                    var hook = resolveFunc();

                    if (hook is null)
                    {
                        throw new NotResolvedException(hookType);
                    }

                    return hook as IAsyncHook;
                });
            }
        }

        private IEnumerable<Lazy<IAsyncHook>> ResolvePostHandleHooks(IReadOnlyCollection<IHookDescriptor> descriptors)
        {
            foreach (var descriptor in descriptors.OrderBy(d => d.Order))
            {
                var hookType = descriptor.HookType;

                var resolveFunc = new Func<object?>(() => _serviceProvider.GetService(hookType));

                yield return new Lazy<IAsyncHook>(() =>
                {
                    var hook = resolveFunc();

                    if (hook is null)
                    {
                        throw new NotResolvedException(hookType);
                    }

                    return hook as IAsyncHook;
                });
            }
        }
    }
}