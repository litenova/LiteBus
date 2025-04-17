using System;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Represents an exception that is thrown when no handler is found for a specific message type.
/// </summary>
/// <remarks>
///     This exception is typically thrown during the mediation process when the system attempts to
///     handle a message but cannot find any registered handler capable of processing it. This can occur
///     if the message type has not been registered in the message registry or if no handler has been
///     registered for the message type or any of its base types or interfaces.
/// </remarks>
[Serializable]
public class NoHandlerFoundException : Exception
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="NoHandlerFoundException" /> class with a message
    ///     that includes the name of the message type for which no handler was found.
    /// </summary>
    /// <param name="messageType">The type of the message for which no handler was found.</param>
    /// <remarks>
    ///     The exception message includes the name of the message type to help diagnose the issue.
    /// </remarks>
    public NoHandlerFoundException(Type messageType)
        : base($"No handler found for message type '{messageType.Name}'")
    {
    }
}