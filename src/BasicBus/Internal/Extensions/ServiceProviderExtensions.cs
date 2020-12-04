using System;
using BasicBus.Abstractions;

namespace BasicBus.Internal.Extensions
{
    public static class ServiceProviderExtensions
    {
        public static IMessageHandler<TMessage, TResult> GetHandler<TMessage, TResult>(
            this IServiceProvider serviceProvider, Type handlerType) where TMessage : IMessage<TResult>
        {
            var resolvedService = serviceProvider.GetService(handlerType);

            return (IMessageHandler<TMessage, TResult>) resolvedService;
        }
    }
}