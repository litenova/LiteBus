using System;
using System.Linq;
using System.Reflection;
using LiteBus.Messaging.Abstractions.Descriptors;
using LiteBus.Messaging.Abstractions.Exceptions;

namespace LiteBus.Messaging.Abstractions.FindStrategies;

public class ActualTypeOrFirstAssignableTypeMessageResolveStrategy : IMessageResolveStrategy
{
    public IMessageDescriptor Find(Type messageType, IMessageRegistry messageRegistry)
    {
        if (messageType.IsGenericType)
        {
            messageType = messageType.GetGenericTypeDefinition();
        }

        var descriptor = messageRegistry.SingleOrDefault(d => d.MessageType == messageType) ??
                         messageRegistry.FirstOrDefault(d => d.MessageType.IsAssignableFrom(messageType));

        if (descriptor is null)
        {
            throw new MessageNotRegisteredException(messageType);
        }

        return descriptor;
    }
}