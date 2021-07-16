using System;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Messaging.Internal.Extensions
{
    public static class DescriptorExtensions
    {
        public static IMessageContext<TMessage, TMessageResult> ToMessageContext<TMessage, TMessageResult>(
            this IMessageTypeDescriptor messageTypeDescriptor, IServiceProvider serviceProvider)
        {
            return new m
        }
    }
}