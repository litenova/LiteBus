using System;
using System.Collections.Generic;
using System.Linq;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Messaging.MediationStrategies;

/// <summary>
///     Resolves exactly one main handler from direct and indirect handler collections.
/// </summary>
internal static class SingleMainHandlerResolver
{
    /// <summary>
    ///     Resolves the single main handler for a message, preferring direct registrations over indirect matches.
    /// </summary>
    /// <param name="messageDependencies">The message dependencies that supply handler collections.</param>
    /// <param name="messageType">The runtime type of the message being mediated, used to name resolution failures.</param>
    /// <returns>The resolved lazy handler instance.</returns>
    /// <exception cref="NoHandlerFoundException">Thrown when no handler is registered.</exception>
    /// <exception cref="MultipleHandlerFoundException">Thrown when more than one handler is registered.</exception>
    /// <exception cref="AmbiguousMessageResolveException">
    ///     Thrown when only indirect handlers exist and the most-derived of them are registered for different message types.
    /// </exception>
    /// <remarks>
    ///     <para>
    ///         Direct handlers registered for the concrete message type take precedence. Indirect handlers registered
    ///         for a base type or interface are considered only when no direct handler survives tag filtering, and among
    ///         them the ones registered for the most-derived type win.
    ///     </para>
    ///     <para>
    ///         The runtime type names the failures rather than the compile-time one. The non-generic mediator overloads
    ///         erase the message to <c>ICommand</c>, <c>IQuery</c>, or <c>IEvent</c>, so reporting that type would point
    ///         the diagnostic at the abstraction instead of the message no handler covers.
    ///     </para>
    /// </remarks>
    public static LazyHandler<IMessageHandler, IMainHandlerDescriptor> Resolve(
        IMessageDependencies messageDependencies,
        Type messageType)
    {
        ArgumentNullException.ThrowIfNull(messageDependencies);
        ArgumentNullException.ThrowIfNull(messageType);

        var handlers = SelectCandidateMainHandlers(messageDependencies, messageType);

        if (handlers.Count == 0)
        {
            throw new NoHandlerFoundException(messageType);
        }

        if (handlers.Count > 1)
        {
            throw new MultipleHandlerFoundException(
                messageType,
                handlers.Select(h => h.Descriptor.HandlerType).ToList());
        }

        return handlers[0];
    }

    /// <summary>
    ///     Selects candidate main handlers, preferring direct registrations and then the most-derived indirect ones.
    /// </summary>
    /// <param name="messageDependencies">The message dependencies that supply handler collections.</param>
    /// <param name="messageType">The runtime type of the message being mediated, used to name resolution failures.</param>
    /// <returns>The candidate main handlers after applying direct-over-indirect and most-derived precedence.</returns>
    /// <exception cref="AmbiguousMessageResolveException">
    ///     Two or more indirect handlers are registered for different message types that are equally derived.
    /// </exception>
    /// <remarks>
    ///     <para>
    ///         A closed generic message's handlers for its own generic definition, such as <c>ListQueryHandler&lt;T&gt;</c>
    ///         for <c>ListQuery&lt;string&gt;</c>, are indirect so that a handler registered for the closed message
    ///         overrides them, but they still belong to the message and win over every base-type handler.
    ///     </para>
    ///     <para>
    ///         Otherwise a message covered only through its base types can have indirect handlers registered for several
    ///         of them, such as one for its base class and one for an interface it implements. The handler registered for
    ///         the most-derived type is chosen, scored the way
    ///         <see cref="ActualTypeOrFirstAssignableTypeMessageResolveStrategy" /> scores registered messages, so the
    ///         same handler runs whether or not the message's own type was registered. Handlers registered for one type
    ///         stay together, so two of them still fail as multiple handlers.
    ///     </para>
    /// </remarks>
    private static List<LazyHandler<IMessageHandler, IMainHandlerDescriptor>> SelectCandidateMainHandlers(
        IMessageDependencies messageDependencies,
        Type messageType)
    {
        var directHandlers = messageDependencies.MainHandlers.ToList();

        if (directHandlers.Count > 0)
        {
            return directHandlers;
        }

        var indirectHandlers = messageDependencies.IndirectMainHandlers.ToList();

        if (indirectHandlers.Count <= 1)
        {
            return indirectHandlers;
        }

        // A handler for the message's own generic definition is the message's handler, not a base type's.
        if (messageType.IsGenericType)
        {
            var ownDefinition = messageType.GetGenericTypeDefinition();
            var forOwnDefinition = indirectHandlers
                .Where(handler => handler.Descriptor.MessageType == ownDefinition)
                .ToList();

            if (forOwnDefinition.Count > 0)
            {
                return forOwnDefinition;
            }
        }

        var bestDepth = indirectHandlers.Max(handler => MessageTypeDepth.Of(handler.Descriptor.MessageType));
        var mostDerived = indirectHandlers
            .Where(handler => MessageTypeDepth.Of(handler.Descriptor.MessageType) == bestDepth)
            .ToList();

        var tiedMessageTypes = mostDerived.Select(handler => handler.Descriptor.MessageType).Distinct().ToList();

        if (tiedMessageTypes.Count > 1)
        {
            throw new AmbiguousMessageResolveException(
                messageType,
                typeof(ActualTypeOrFirstAssignableTypeMessageResolveStrategy),
                tiedMessageTypes);
        }

        return mostDerived;
    }
}
