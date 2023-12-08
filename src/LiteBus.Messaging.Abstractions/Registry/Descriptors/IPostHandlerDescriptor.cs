using System;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
/// Represents a descriptor for a post-handler, providing metadata about the handler such as the message type it handles,
/// its execution order, any associated tags, and the type of the result that is expected by the user as an argument.
/// </summary>
public interface IPostHandlerDescriptor : IHandlerDescriptor
{
    /// <summary>
    /// Gets the type of the result produced by the main handler that is associated with this post-handler.
    /// </summary>
    Type MessageResultType { get; }
}