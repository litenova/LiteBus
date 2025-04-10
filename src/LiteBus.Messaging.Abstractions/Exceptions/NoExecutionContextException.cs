using System;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
/// Represents an exception that is thrown when an operation requires an execution context,
/// but no execution context is set in the ambient context.
/// </summary>
/// <remarks>
/// This exception is typically thrown when a handler method attempts to access the ambient execution context
/// (via <see cref="AmbientExecutionContext.Current"/>), but no context has been set.
/// This can occur if a handler method is called outside of the normal mediation process,
/// or if the ambient execution context has been improperly cleared or not set.
/// </remarks>
public sealed class NoExecutionContextException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="NoExecutionContextException"/> class with a default message.
    /// </summary>
    /// <remarks>
    /// The default message indicates that no execution context is set, which helps diagnose the issue.
    /// </remarks>
    public NoExecutionContextException()
        : base("No execution context is set")
    {
    }
}