using System;
using System.Collections.Generic;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Messaging;

/// <summary>
///     Implements a message resolve strategy that first attempts to find a descriptor for the exact message type,
///     and if not found, returns the most-derived descriptor for a type that is assignable from the message type.
/// </summary>
/// <remarks>
///     This strategy is useful for handling inheritance and interface implementation in the messaging system.
///     It allows messages to be handled by handlers registered for their exact type or for any base type or interface
///     that they implement. Resolution order is exact type first, then the most-derived assignable registered type
///     using an inheritance depth score (base-type chain length plus the registered type's implemented interface
///     count; generic type definitions contribute only the chain length). When multiple assignable types share the
///     same depth score, an <see cref="AmbiguousMessageResolveException" /> is thrown.
/// </remarks>
public sealed class ActualTypeOrFirstAssignableTypeMessageResolveStrategy : IMessageResolveStrategy
{
    /// <inheritdoc />
    public IMessageDescriptor? Find(Type messageType, IMessageReader messageReader)
    {
        var descriptor = messageReader.Find(messageType);

        if (descriptor is not null)
        {
            return descriptor;
        }

        IMessageDescriptor? bestMatch = null;
        var bestDepth = -1;
        var ambiguousMatches = 0;
        List<Type>? tiedCandidates = null;

        foreach (var candidate in messageReader)
        {
            if (!candidate.MessageType.IsAssignableFrom(messageType))
            {
                continue;
            }

            var depth = MessageTypeDepth.Of(candidate.MessageType);

            if (depth > bestDepth)
            {
                bestDepth = depth;
                bestMatch = candidate;
                ambiguousMatches = 1;
                tiedCandidates = [candidate.MessageType];
                continue;
            }

            if (depth == bestDepth)
            {
                ambiguousMatches++;
                tiedCandidates?.Add(candidate.MessageType);
            }
        }

        if (ambiguousMatches > 1)
        {
            throw new AmbiguousMessageResolveException(
                messageType,
                typeof(ActualTypeOrFirstAssignableTypeMessageResolveStrategy),
                tiedCandidates ?? []);
        }

        return bestMatch;
    }
}
