using System;

namespace LiteBus.Messaging.Exceptions;

/// <summary>
///     Thrown when more than one main handler is registered for the same message type.
/// </summary>
[Serializable]
public class MultipleMessageHandlerFoundException : Exception
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="MultipleMessageHandlerFoundException" /> class.
    /// </summary>
    /// <param name="messageName">The message type name that has multiple handlers.</param>
    public MultipleMessageHandlerFoundException(string messageName) :
        base($"Multiple handler found for {messageName}.")
    {
    }
}
