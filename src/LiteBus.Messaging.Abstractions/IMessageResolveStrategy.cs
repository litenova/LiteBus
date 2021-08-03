using System;
using LiteBus.Messaging.Abstractions.Descriptors;

namespace LiteBus.Messaging.Abstractions
{
    public interface IMessageResolveStrategy<in TMessage>
    {
        IMessageDescriptor Find(TMessage message, IMessageRegistry messageRegistry);
    }
}