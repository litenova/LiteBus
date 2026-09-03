using System;
using System.Collections.Generic;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Read-only surface for message descriptor lookup.
///     Consumed by the mediator and resolve strategies at runtime.
/// </summary>
/// <remarks>
///     Implementations must provide O(1) exact-type lookup through <see cref="Find" />.
///     Enumeration supports assignability-based fallback resolution.
/// </remarks>
public interface IMessageReader : IEnumerable<IMessageDescriptor>
{
    /// <summary>
    ///     Gets all handler descriptors in registration order.
    /// </summary>
    IReadOnlyList<IHandlerDescriptor> Handlers { get; }

    /// <summary>
    ///     Gets the number of committed message descriptors in the registry.
    /// </summary>
    int Count { get; }

    /// <summary>
    ///     Gets each registered open generic handler and the concrete message types it was closed over.
    /// </summary>
    /// <value>
    ///     One entry per open generic handler, or an empty dictionary for a reader that does not close open generics.
    /// </value>
    /// <remarks>
    ///     <para>
    ///         Exposed so composition can report it. An open generic handler discovered by assembly scanning inserts a
    ///         pipeline stage into every message it fits, with no registration line for a reviewer to read, which makes
    ///         it the most powerful implicit behavior in the library. Naming each one and its closure count turns that
    ///         from invisible into a line in the startup log.
    ///     </para>
    ///     <para>
    ///         The default implementation returns nothing, so a custom reader or a test double compiles unchanged.
    ///     </para>
    /// </remarks>
    IReadOnlyDictionary<Type, IReadOnlyCollection<Type>> OpenGenericClosures =>
        new Dictionary<Type, IReadOnlyCollection<Type>>();

    /// <summary>
    ///     Gets the open generic handler types that arrived through an assembly scan rather than being named by the
    ///     composition code.
    /// </summary>
    /// <value>
    ///     The scanned handler types, or an empty collection for a reader that does not distinguish the two.
    /// </value>
    /// <remarks>
    ///     Read by <c>RequireExplicitOpenGenerics</c>. The default implementation returns nothing, which means the
    ///     strict check passes for a custom reader rather than failing on something it cannot see.
    /// </remarks>
    IReadOnlyCollection<Type> ScannedOpenGenericHandlers => [];

    /// <summary>
    ///     Returns the descriptor for <paramref name="messageType" /> using an exact type match,
    ///     or <see langword="null" /> when no descriptor is registered for that type.
    /// </summary>
    /// <param name="messageType">The message type to resolve, normalized to its generic type definition when generic.</param>
    /// <returns>The registered descriptor, or <see langword="null" /> when not found.</returns>
    IMessageDescriptor? Find(Type messageType);
}