using System;
using System.Collections.Generic;

namespace LiteBus.Messaging.Abstractions.Extensions
{
    public static class ServiceProviderExtensions
    {
        public static IEnumerable<ISyncMessageHandler<TMessage, TMessageResult>> GetHandlers<TMessage, TMessageResult>(
            this IServiceProvider serviceProvider,
            IEnumerable<Type> handlerTypes) where TMessage : IMessage
        {
            foreach (var handlerType in handlerTypes)
            {
                var resolvedService = serviceProvider.GetService(handlerType);

                yield return (ISyncMessageHandler<TMessage, TMessageResult>) resolvedService;
            }
        }

        public static IEnumerable<IPostHandleHook> GetPostHandleHooks(this IServiceProvider serviceProvider,
                                                                      IEnumerable<Type> hookTypes)
        {
            foreach (var hookType in hookTypes)
            {
                var resolvedService = serviceProvider.GetService(hookType);

                yield return (IPostHandleHook) resolvedService;
            }
        }

        public static IEnumerable<IPreHandleHook> GetPreHandleHooks(this IServiceProvider serviceProvider,
                                                                    IEnumerable<Type> hookTypes)
        {
            foreach (var hookType in hookTypes)
            {
                var resolvedService = serviceProvider.GetService(hookType);

                yield return (IPreHandleHook) resolvedService;
            }
        }
        
        public static ISyncMessageHandler GetHandler(this IServiceProvider serviceProvider, Type handlerType)
        {
            return serviceProvider.GetService(handlerType) as ISyncMessageHandler;
        }
    }
}