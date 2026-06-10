using System;
using System.Collections.Generic;
using System.Linq;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Represents an exception that is thrown when multiple handlers are found for a message type
///     that should have only one handler.
/// </summary>
/// <remarks>
///     This exception is typically thrown during the mediation process when using a mediation strategy
///     that expects a single handler for a message type, but multiple handlers are registered.
///     This can occur in command handling scenarios where each command should have exactly one handler.
/// </remarks>
[Serializable]
public class MultipleHandlerFoundException : Exception
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="MultipleHandlerFoundException" /> class with a message
    ///     that includes the message type and the handler implementation types that were found.
    /// </summary>
    /// <param name="messageType">The type of the message for which multiple handlers were found.</param>
    /// <param name="handlerTypes">The handler implementation types registered for the message.</param>
    /// <remarks>
    ///     The exception message includes the message type and handler type names to help diagnose the issue.
    /// </remarks>
    public MultipleHandlerFoundException(Type messageType, IReadOnlyList<Type> handlerTypes)
        : base(BuildMessage(messageType, handlerTypes))
    {
        MessageType = messageType;
        HandlerTypes = handlerTypes;
    }

    /// <summary>
    ///     Gets the message type for which multiple handlers were found.
    /// </summary>
    public Type MessageType { get; }

    /// <summary>
    ///     Gets the handler implementation types registered for the message.
    /// </summary>
    public IReadOnlyList<Type> HandlerTypes { get; }

    /// <summary>
    ///     Builds the exception message from the message type and handler implementation types.
    /// </summary>
    /// <param name="messageType">The message type.</param>
    /// <param name="handlerTypes">The handler implementation types.</param>
    /// <returns>The formatted exception message.</returns>
    private static string BuildMessage(Type messageType, IReadOnlyList<Type> handlerTypes)
    {
        var handlerNames = handlerTypes.Count == 0
            ? "(none)"
            : string.Join(", ", handlerTypes.Select(t => t.FullName ?? t.Name));

        return
            $"Message type '{messageType.FullName ?? messageType.Name}' has {handlerTypes.Count} handlers registered: {handlerNames}. " +
            "Single-handler mediation requires exactly one handler.";
    }
}
