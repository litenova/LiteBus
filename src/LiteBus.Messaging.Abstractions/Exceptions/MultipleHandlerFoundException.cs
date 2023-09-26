using System;

namespace LiteBus.Messaging.Abstractions;

[Serializable]
internal class MultipleHandlerFoundException : Exception
{
    public MultipleHandlerFoundException(Type messageType, int numberOfHandlers) : base($"{messageType.Name} has {numberOfHandlers} handlers registered.")
    {
    }
}