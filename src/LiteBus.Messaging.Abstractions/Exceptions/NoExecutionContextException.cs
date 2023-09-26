using System;

namespace LiteBus.Messaging.Abstractions;

public sealed class NoExecutionContextException : Exception
{
    public NoExecutionContextException() : base("No execution context is set")
    {
    }
}