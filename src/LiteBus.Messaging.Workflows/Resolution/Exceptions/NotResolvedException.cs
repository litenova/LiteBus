using System;

namespace LiteBus.Messaging.Workflows.Resolution.Exceptions;

[Serializable]
public class NotResolvedException : Exception
{
    public NotResolvedException(Type type) : base($"The type of '{type.Name}' could not be resolved")
    {
    }
}