using System.Linq;
using LiteBus.Messaging.Abstractions.Descriptors;
using LiteBus.Messaging.Abstractions.Exceptions;

namespace LiteBus.Messaging.Abstractions.FindStrategies
{
    public class ActualTypeOrBaseTypeMessageResolveStrategy<TMessage> : IMessageResolveStrategy<TMessage>
    {
        public IMessageDescriptor Find(TMessage message, IMessageRegistry messageRegistry)
        {
            var messageType = message.GetType();
            
            var descriptor = messageRegistry.SingleOrDefault(d => d.MessageType == messageType) ??
                             messageRegistry.SingleOrDefault(d => d.MessageType.BaseType == messageType);

            if (descriptor is null)
            {
                throw new MessageNotRegisteredException(typeof(TMessage));    
            }

            return descriptor;
        }
    }
}