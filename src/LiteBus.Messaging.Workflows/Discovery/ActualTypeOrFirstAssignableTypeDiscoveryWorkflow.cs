using System;
using System.Linq;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.Metadata;
using LiteBus.Messaging.Workflows.Discovery.Exceptions;

namespace LiteBus.Messaging.Workflows.Discovery;

public class ActualTypeOrFirstAssignableTypeDiscoveryWorkflow : IDiscoveryWorkflow
{
    public IMessageDescriptor Discover(IMessageRegistry messageRegistry, Type messageType)
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