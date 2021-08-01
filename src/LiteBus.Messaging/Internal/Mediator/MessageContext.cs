using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Internal.Exceptions;
using LiteBus.Messaging.Internal.Extensions;

namespace LiteBus.Messaging.Internal.Mediator
{
    // A handler Handles a <Message> and returns <Message Result> 
    
    // Message -> Mediator -> Message -> 
    
    // Message 
    // Handler
    // 
    
    public class MessageContext<TMessage, TMessageResult> : IMessageContext<TMessage, TMessageResult>
    {
        private readonly IServiceProvider _serviceProvider;

        public MessageContext(IMessageDescriptor descriptor, IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;

            Handlers = ResolveHandlers(descriptor.HandlerTypes).ToLazyReadOnlyCollection();
            PostHandleHooks = ResolvePostHandleHooks(descriptor.PostHandleHookDescriptors).ToLazyReadOnlyCollection();
            PreHandleHooks = ResolvePreHandleHooks(descriptor.PreHandleHookDescriptors).ToLazyReadOnlyCollection();
        }

        private IEnumerable<Lazy<IMessageHandler<TMessage, TMessageResult>>> ResolveHandlers(
            IEnumerable<Type> handlerTypes)
        {
            foreach (var handlerType in handlerTypes)
            {
                var resolveFunc = new Func<object?>(() => _serviceProvider.GetService(handlerType));

                yield return new(() =>
                {
                    var handler = resolveFunc();

                    if (handler is null)
                    {
                        throw new NotResolvedException(handlerType);
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