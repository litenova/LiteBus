using System;
using System.Collections.Generic;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.Descriptors;
using LiteBus.Messaging.Internal.Exceptions;
using LiteBus.Messaging.Internal.Extensions;

namespace LiteBus.Messaging.Internal.Mediator
{
    public class MessageContext<TMessage, TMessageResult> : IMessageContext<TMessage, TMessageResult>
    {
        private readonly IServiceProvider _serviceProvider;

        public MessageContext(IMessageDescriptor descriptor, IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;

            Handlers = ResolveHandlers(descriptor.HandlerDescriptors).ToLazyReadOnlyCollection();
            PostHandleHooks = ResolvePostHandleHooks(descriptor.PostHandleHookDescriptors).ToLazyReadOnlyCollection();
            PreHandleHooks = ResolvePreHandleHooks(descriptor.PreHandleHookDescriptors).ToLazyReadOnlyCollection();
        }

        private IEnumerable<Lazy<IMessageHandler<TMessage, TMessageResult>>> ResolveHandlers(
            IReadOnlyCollection<IHandlerDescriptor> descriptors)
        {
            foreach (var descriptor in descriptors)
            {
                var resolveFunc = new Func<object?>(() => _serviceProvider.GetService(descriptor.HandlerType));

                yield return new(() =>
                {
                    var handler = resolveFunc();

                    if (handler is null)
                    {
                        throw new NotResolvedException(descriptor.HandlerType);
                    }

                    return null;
                });
            }
        }

        private IEnumerable<Lazy<IPreHandleHook<TMessage>>> ResolvePreHandleHooks(
            IReadOnlyCollection<IHookDescriptor> descriptors)
        {
            foreach (var descriptor in descriptors)
            {
                var hookType = descriptor.HookType;

                var resolveFunc = new Func<object?>(() => _serviceProvider.GetService(hookType));

                yield return new(() =>
                {
                    var hook = resolveFunc();

                    if (hook is null)
                    {
                        throw new NotResolvedException(hookType);
                    }

                    var result = hook as IPreHandleHook<TMessage>;

                    return result;
                });
            }
        }

        private IEnumerable<Lazy<IPostHandleHook<TMessage>>> ResolvePostHandleHooks(
            IReadOnlyCollection<IHookDescriptor> descriptors)
        {
            foreach (var descriptor in descriptors)
            {
                var hookType = descriptor.HookType;

                var resolveFunc = new Func<object?>(() => _serviceProvider.GetService(hookType));

                yield return new(() =>
                {
                    var hook = resolveFunc();

                    if (hook is null)
                    {
                        throw new NotResolvedException(hookType);
                    }

                    return hook as IPostHandleHook<TMessage>;
                });
            }
        }

        public ILazyReadOnlyCollection<IMessageHandler<TMessage, TMessageResult>> Handlers { get; }

        public ILazyReadOnlyCollection<IPostHandleHook<TMessage>> PostHandleHooks { get; }

        public ILazyReadOnlyCollection<IPreHandleHook<TMessage>> PreHandleHooks { get; }
    }
}