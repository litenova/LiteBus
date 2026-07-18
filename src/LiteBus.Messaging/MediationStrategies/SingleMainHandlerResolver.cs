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
    /// <typeparam name="TMessage">The message type being mediated.</typeparam>
    /// <param name="messageDependencies">The message dependencies that supply handler collections.</param>
    /// <returns>The resolved lazy handler instance.</returns>
    /// <exception cref="NoHandlerFoundException">Thrown when no handler is registered.</exception>
    /// <exception cref="MultipleHandlerFoundException">Thrown when more than one handler is registered.</exception>
    /// <remarks>
    ///     Direct handlers registered for the concrete message type take precedence. Indirect handlers registered for a
    ///     base type or interface are considered only when no direct handler survives tag filtering.
    /// </remarks>
    public static LazyHandler<IMessageHandler, IMainHandlerDescriptor> Resolve<TMessage>(
        IMessageDependencies messageDependencies)
        where TMessage : notnull
    {
        ArgumentNullException.ThrowIfNull(messageDependencies);

        var handlers = SelectCandidateMainHandlers(messageDependencies);

        if (handlers.Count == 0)
        {
            throw new NoHandlerFoundException(typeof(TMessage));
        }

        if (handlers.Count > 1)
        {
            throw new MultipleHandlerFoundException(
                typeof(TMessage),
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