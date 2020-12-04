using System;
using Paykan.Abstractions;

namespace Paykan.Internal.Extensions
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