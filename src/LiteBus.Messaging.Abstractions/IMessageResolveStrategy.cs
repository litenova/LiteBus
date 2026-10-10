using System;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Defines a strategy for resolving message descriptors from a message reader based on a message type.
/// </summary>
/// <remarks>
///     Message resolve strategies determine how a message type is matched to a message descriptor in the registry.
///     Different strategies can implement different matching rules, such as exact type matching, assignable type matching,
///     or more complex rules based on message attributes or other criteria.
/// </remarks>
public interface IMessageResolveStrategy
{
    /// <summary>
    ///     Finds a message descriptor for the specified message type.
    /// </summary>
    /// <param name="messageType">The type of the message to find a descriptor for.</param>
    /// <param name="messageReader">The message reader to search.</param>
    /// <returns>
    ///     The message descriptor if found; otherwise, <see langword="null" />.
    /// </returns>
    /// <remarks>
    ///     The implementation determines the specific rules for matching a message type to a descriptor.
    ///     For example, it might look for an exact type match, or it might consider inheritance relationships.
    /// </remarks>
    IMessageDescriptor? Find(Type messageType, IMessageReader messageReader);
}
