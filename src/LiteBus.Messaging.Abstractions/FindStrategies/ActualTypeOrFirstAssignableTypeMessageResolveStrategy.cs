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
    /// <inheritdoc />
    public IMessageDescriptor? Find(Type messageType, IMessageReader messageReader)
    {
        var lookupType = messageType.IsGenericType
            ? messageType.GetGenericTypeDefinition()
            : messageType;

        var descriptor = messageReader.Find(lookupType);
        if (descriptor is not null)
        {
            return descriptor;
        }

        return messageReader.FirstOrDefault(d => d.MessageType.IsAssignableFrom(messageType));
    }
}
