using System;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Messaging.Internal.Extensions
{
    public static class DescriptorExtensions
    {
        public static IMessageContext<TMessage, TMessageResult> ToMessageContext<TMessage, TMessageResult>(
            this IMessageDescriptor messageDescriptor, IServiceProvider serviceProvider)
        {
            return new m
        }
    }
}