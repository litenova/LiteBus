using System;

namespace LiteBus.Messaging.Abstractions;

[Serializable]
public class MultipleHandlerFoundException : Exception
{
    public MultipleHandlerFoundException(Type messageType, int numberOfHandlers) : base($"{messageType.Name} has {numberOfHandlers} handlers registered.")
    {
    }
}