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
    /// <remarks>
    ///     <para>
    ///         Direct handlers registered for the concrete message type take precedence. Indirect handlers registered
    ///         for a base type or interface are considered only when no direct handler survives tag filtering.
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

        var handlers = SelectCandidateMainHandlers(messageDependencies);

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
    ///     Selects candidate main handlers, preferring direct registrations when any are available.
    /// </summary>
    /// <param name="messageDependencies">The message dependencies that supply handler collections.</param>
    /// <returns>The candidate main handlers after applying direct-over-indirect precedence.</returns>
    private static List<LazyHandler<IMessageHandler, IMainHandlerDescriptor>> SelectCandidateMainHandlers(
        IMessageDependencies messageDependencies)
    {
        var directHandlers = messageDependencies.MainHandlers.ToList();

        if (directHandlers.Count > 0)
        {
            return directHandlers;
        }

        return messageDependencies.IndirectMainHandlers.ToList();
    }
}
