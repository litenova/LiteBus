using System;
using System.Linq;
using LiteBus.Messaging.Abstractions.Descriptors;
using LiteBus.Messaging.Abstractions.Exceptions;

namespace LiteBus.Messaging.Abstractions.FindStrategies
{
    public class ActualTypeOrBaseTypeMessageResolveStrategy : IMessageResolveStrategy
    {
        public IMessageDescriptor Find(Type messageType, IMessageRegistry messageRegistry)
        {
            var descriptor = messageRegistry.SingleOrDefault(d => d.MessageType == messageType) ??
                             messageRegistry.SingleOrDefault(d => d.MessageType.BaseType == messageType);

            if (descriptor is null)
            {
                throw new MessageNotRegisteredException(messageType);
            }

            return descriptor;
        }
    }
}