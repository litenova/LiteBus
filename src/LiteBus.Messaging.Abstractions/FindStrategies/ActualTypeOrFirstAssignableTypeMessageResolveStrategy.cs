#nullable enable
using System;
using System.Linq;

namespace LiteBus.Messaging.Abstractions;

public sealed class ActualTypeOrFirstAssignableTypeMessageResolveStrategy : IMessageResolveStrategy
{
    public IMessageDescriptor? Find(Type messageType, IMessageRegistry messageRegistry)
    {
        if (messageType.IsGenericType)
        {
            messageType = messageType.GetGenericTypeDefinition();
        }

        var descriptor = messageRegistry.SingleOrDefault(d => d.MessageType == messageType) ??
                         messageRegistry.FirstOrDefault(d => d.MessageType.IsAssignableFrom(messageType));

        return descriptor;
    }
}