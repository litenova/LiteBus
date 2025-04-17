using System;
using System.Linq;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Implements a message resolve strategy that first attempts to find a descriptor for the exact message type,
///     and if not found, returns the first descriptor for a type that is assignable from the message type.
/// </summary>
/// <remarks>
///     This strategy is useful for handling inheritance and interface implementation in the messaging system.
///     It allows messages to be handled by handlers registered for their exact type or for any base type or interface
///     that they implement. When multiple assignable types are found, the first one is returned.
/// </remarks>
public sealed class ActualTypeOrFirstAssignableTypeMessageResolveStrategy : IMessageResolveStrategy
{
    /// <summary>
    ///     Finds a message descriptor for the specified message type from the message registry.
    /// </summary>
    /// <param name="messageType">The type of the message to find a descriptor for.</param>
    /// <param name="messageRegistry">The message registry to search in.</param>
    /// <returns>
    ///     The message descriptor for the exact message type if found; otherwise, the first descriptor
    ///     for a type that is assignable from the message type; or <c>null</c> if no suitable descriptor is found.
    /// </returns>
    /// <remarks>
    ///     For generic types, this method uses the generic type definition for matching.
    /// </remarks>
    public IMessageDescriptor? Find(Type messageType, IMessageRegistry messageRegistry)
    {
        if (messageType.IsGenericType)
        {
            messageType = messageType.GetGenericTypeDefinition();
        }

        var descriptor = messageRegistry.SingleOrDefault(d => d.MessageType == messageType) ?? messageRegistry.FirstOrDefault(d => d.MessageType.IsAssignableFrom(messageType));

        return descriptor;
    }
}