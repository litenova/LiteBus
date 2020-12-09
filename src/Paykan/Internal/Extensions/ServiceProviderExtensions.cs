using System;
using System.Collections.Generic;
using Paykan.Abstractions;
using Paykan.Abstractions.Interceptors;

namespace Paykan.Internal.Extensions
{
    public static class ServiceProviderExtensions
    {
        public static IMessageHandler<TMessage, TResult> GetHandler<TMessage, TResult>(
            this IServiceProvider serviceProvider,
            Type handlerType) where TMessage : IMessage<TResult>
        {
            var resolvedService = serviceProvider.GetService(handlerType);

            return (IMessageHandler<TMessage, TResult>) resolvedService;
        }

        public static IEnumerable<IMessageHandler<TMessage, TResult>> GetHandlers<TMessage, TResult>(
            this IServiceProvider serviceProvider,
            IEnumerable<Type> handlerTypes) where TMessage : IMessage<TResult>
        {
            foreach (var handlerType in handlerTypes)
            {
                var resolvedService = serviceProvider.GetService(handlerType);

                yield return (IMessageHandler<TMessage, TResult>) resolvedService;
            }
        }

        public static IEnumerable<IPostHandleHook<TMessage>> GetPostHandleHooks<TMessage>(this IServiceProvider serviceProvider,
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