using System;

namespace LiteBus.Messaging.Abstractions.Exceptions;

[Serializable]
internal class MultipleHandlerFoundException : Exception
{
    public MultipleHandlerFoundException(Type messageType)
        : base($"Multiple handler found for {messageType}.")
    {
    }
}