using System;
using System.Collections.Generic;

namespace LiteBus.Messaging.Abstractions.Extensions
{
    public static class ServiceProviderExtensions
    {
        public static IMessageHandler<TMessage, TMessageResult> GetHandler<TMessage, TMessageResult>(
            this IServiceProvider serviceProvider,
            Type handlerType)
        {
            var resolvedService = serviceProvider.GetService(handlerType);

            return (IMessageHandler<TMessage, TMessageResult>) resolvedService;
        }

        public static IEnumerable<IMessageHandler<TMessage, TMessageResult>> GetHandlers<TMessage, TMessageResult>(
            this IServiceProvider serviceProvider,
            IEnumerable<Type> handlerTypes)
        {
            foreach (var handlerType in handlerTypes)
            {
                var resolvedService = serviceProvider.GetService(handlerType);

                yield return (IMessageHandler<TMessage, TMessageResult>) resolvedService;
            }
        }

        public static IEnumerable<IPostHandleHook<TMessage>> GetPostHandleHooks<TMessage>(
            this IServiceProvider serviceProvider,
            IEnumerable<Type> hookTypes)
            where TMessage : IMessage
        {
            foreach (var hookType in hookTypes)
            {
                var resolvedService = serviceProvider.GetService(hookType);

                yield return (IPostHandleHook<TMessage>) resolvedService;
            }
        }
    }
}