using System;
using System.Collections.Generic;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
/// Represents a registry that maintains information about messages and their associated handlers.
/// </summary>
/// <remarks>
/// The message registry is responsible for tracking all registered message types and their descriptors.
/// It provides access to message descriptors which contain information about handlers, pre-handlers,
/// post-handlers, and error handlers associated with each message type.
/// </remarks>
public interface IMessageRegistry : IReadOnlyCollection<IMessageDescriptor>
{
    /// <summary>
    /// Registers a type with the message registry.
    /// </summary>
    /// <param name="type">The type to register. This can be a message type, handler type, or any other type
    /// that should be recognized by the messaging system.</param>
    /// <remarks>
    /// When a type is registered, the registry analyzes it to determine if it's a message, handler,
    /// pre-handler, post-handler, or error handler, and updates its internal state accordingly.
    /// </remarks>
    void Register(Type type);
}