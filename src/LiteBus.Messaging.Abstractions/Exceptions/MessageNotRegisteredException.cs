using System;

namespace LiteBus.Messaging.Abstractions;

[Serializable]
internal class MessageNotRegisteredException : Exception
{
    public MessageNotRegisteredException(Type messageType) :
        base($"The message type '{messageType.Name}' is not registered")
    {
    }
}