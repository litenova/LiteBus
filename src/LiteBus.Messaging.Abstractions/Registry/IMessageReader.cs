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
    ///     Returns the descriptor for <paramref name="messageType" /> using an exact type match,
    ///     or <see langword="null" /> when no descriptor is registered for that type.
    /// </summary>
    /// <param name="messageType">The message type to resolve, normalized to its generic type definition when generic.</param>
    /// <returns>The registered descriptor, or <see langword="null" /> when not found.</returns>
    IMessageDescriptor? Find(Type messageType);
}