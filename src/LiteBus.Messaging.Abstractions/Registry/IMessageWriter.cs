using System;
using System.Diagnostics.CodeAnalysis;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Write-only surface for message and handler type registration.
///     Consumed by module builders at configuration time.
/// </summary>
/// <remarks>
///     Accepts handler types, message types, and open generic handler types.
///     The registry determines the appropriate descriptor building strategy for each registered type.
/// </remarks>
public interface IMessageWriter
{
    /// <summary>
    ///     Registers a type with the message system.
    /// </summary>
    /// <param name="type">
    ///     The type to register. When the type implements a handler interface, handler descriptors are built
    ///     and linked to relevant message descriptors. When the type is a plain message type, a message descriptor
    ///     is created and linked to handlers already registered.
    /// </param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="type" /> is <see langword="null" />.</exception>
    [RequiresUnreferencedCode("Handler and message registration inspects CLR types via reflection.")]
    void Register(
        [DynamicallyAccessedMembers(
            DynamicallyAccessedMemberTypes.PublicConstructors
            | DynamicallyAccessedMemberTypes.PublicMethods
            | DynamicallyAccessedMemberTypes.Interfaces)]
        Type type);
}