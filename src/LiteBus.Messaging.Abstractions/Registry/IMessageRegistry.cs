using System;
using System.Collections.Generic;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Defines the contract for a message registry that manages handler registration and provides access to registered
///     messages.
/// </summary>
public interface IMessageRegistry : IReadOnlyCollection<IMessageDescriptor>
{
    /// <summary>
    ///     Gets a read-only list of all registered handler descriptors in the order they were registered.
    /// </summary>
    /// <value>
    ///     A read-only collection containing all handler descriptors that have been registered with this registry.
    /// </value>
    /// <remarks>
    ///     The handlers are returned in the order they were registered, which may be important for
    ///     execution order in scenarios where multiple handlers exist for the same message type.
    /// </remarks>
    IReadOnlyList<IHandlerDescriptor> Handlers { get; }

    /// <summary>
    ///     Registers a type with the message registry for handler discovery and message processing.
    /// </summary>
    /// <param name="type">The type to register. This can be a message type, handler type, or other registrable construct.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="type" /> is <see langword="null" />.</exception>
    /// <remarks>
    ///     <para>
    ///         This method performs automatic discovery of the type's role within the messaging system.
    ///         If the type implements handler interfaces, it will be registered as a handler.
    ///         If the type is a message type, it will be registered for message resolution.
    ///     </para>
    ///     <para>
    ///         Duplicate registrations of the same type are ignored to prevent duplicate handler registrations.
    ///         Generic types are automatically normalized to their generic type definitions for proper resolution.
    ///     </para>
    ///     <para>
    ///         System types (those in the System namespace) are automatically excluded from registration
    ///         to prevent unnecessary processing of framework types.
    ///     </para>
    /// </remarks>
    /// <example>
    ///     <code>
    /// // Register a command handler
    /// registry.Register(typeof(CreateUserCommandHandler));
    /// 
    /// // Register a message type
    /// registry.Register(typeof(UserCreatedEvent));
    /// 
    /// // Register from assembly
    /// foreach (var type in assembly.GetTypes())
    /// {
    ///     registry.Register(type);
    /// }
    /// </code>
    /// </example>
    void Register(Type type);

    /// <summary>
    ///     Clears all registered messages and handlers from the registry, resetting it to an empty state.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         This method removes all previously registered message descriptors, handler descriptors,
    ///         and internal tracking state from the registry. After calling this method, the registry
    ///         will be in the same state as a newly created instance.
    ///     </para>
    ///     <para>
    ///         This operation is primarily intended for testing scenarios where registry isolation
    ///         is required between test runs. In production applications, the registry is typically
    ///         populated once during application startup and used throughout the application lifetime.
    ///     </para>
    ///     <para>
    ///         <strong>Warning:</strong> Calling this method will invalidate any existing references
    ///         to message descriptors or handler collections obtained from this registry.
    ///     </para>
    /// </remarks>
    /// <example>
    ///     <code>
    /// // Clear registry for test isolation
    /// registry.Clear();
    /// 
    /// // Registry is now empty and ready for new registrations
    /// Assert.Equal(0, registry.Count);
    /// Assert.Equal(0, registry.Handlers.Count);
    /// </code>
    /// </example>
    void Clear();
}