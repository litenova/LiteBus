using System;
using System.Collections.Generic;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Represents a descriptor for a handler, providing metadata about the handler such as the message type it handles,
///     its execution order, and any associated tags.
/// </summary>
public interface IHandlerDescriptor
{
    /// <summary>
    ///     Gets the type of the message that the handler is associated with. If the message type is generic,
    ///     this property returns the generic type definition.
    /// </summary>
    Type MessageType { get; }

    /// <summary>
    ///     Gets the order in which the handler should be executed. Handlers with lower order values are executed first.
    /// </summary>
    int Order { get; }

    /// <summary>
    ///     Gets a collection of tags associated with the handler. Tags can be used to categorize or identify handlers in a
    ///     flexible way.
    /// </summary>
    IReadOnlyCollection<string> Tags { get; }

    /// <summary>
    ///     Gets the type of the handler. This represents the actual implementation type of the handler.
    /// </summary>
    Type HandlerType { get; }
}