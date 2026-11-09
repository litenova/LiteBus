using System;

namespace LiteBus.Messaging;

/// <summary>
///     Scores how derived a registered message type is, so the most-derived of several candidates can be preferred.
/// </summary>
/// <remarks>
///     <see cref="ActualTypeOrFirstAssignableTypeMessageResolveStrategy" /> ranks registered message types by this score,
///     and the main-handler resolver ranks base-type handlers by the type each is registered for. Both use the same score,
///     so a message reaches the same handler whether or not its own type was registered.
/// </remarks>
internal static class MessageTypeDepth
{
    /// <summary>
    ///     Computes the depth score of a registered message type.
    /// </summary>
    /// <param name="registeredMessageType">The message type a descriptor or handler is registered for.</param>
    /// <returns>
    ///     The base-type chain length plus the type's implemented interface count, where larger values indicate
    ///     more-derived types. A generic type definition contributes only the chain length.
    /// </returns>
    public static int Of(Type registeredMessageType)
    {
        ArgumentNullException.ThrowIfNull(registeredMessageType);

        var depth = 0;
        var current = registeredMessageType;

        while (current.BaseType is not null)
        {
            depth++;
            current = current.BaseType;
        }

        if (!registeredMessageType.IsGenericTypeDefinition)
        {
            depth += registeredMessageType.GetInterfaces().Length;
        }

        return depth;
    }
}
