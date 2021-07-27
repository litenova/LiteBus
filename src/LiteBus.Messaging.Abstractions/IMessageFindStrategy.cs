using System;

namespace LiteBus.Messaging.Abstractions
{
    public interface IMessageFindStrategy<in TMessage>
    {
        IMessageDescriptor Find(TMessage message,
                                IMessageRegistry messageRegistry);
    }
}