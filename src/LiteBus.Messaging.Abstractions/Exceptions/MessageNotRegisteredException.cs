using System;

namespace LiteBus.Messaging.Abstractions;

[Serializable]
public class NoHandlerFoundException : Exception
{
    public NoHandlerFoundException(Type messageType) : base($"No handler found for message type '{messageType.Name}'")
    {
    }
}