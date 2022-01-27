using System;

namespace LiteBus.Messaging.Workflows.Execution.Exceptions;

[Serializable]
internal class MultipleHandlerFoundException : Exception
{
    public MultipleHandlerFoundException(Type messageType)
        : base($"Multiple handler found for {messageType}.")
    {
    }
}