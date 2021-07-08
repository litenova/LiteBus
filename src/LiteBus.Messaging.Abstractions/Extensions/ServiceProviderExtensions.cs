using System;
using System.Collections.Generic;

namespace LiteBus.Messaging.Abstractions.Extensions
{
    public static class ServiceProviderExtensions
    {
        public static IEnumerable<IMessageHandler> GetHandlers(this IServiceProvider serviceProvider,
                                                               IEnumerable<Type> handlerTypes)
        {
            foreach (var handlerType in handlerTypes)
            {
                var resolvedService = serviceProvider.GetService(handlerType);

                yield return resolvedService as IMessageHandler;
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
    }
}