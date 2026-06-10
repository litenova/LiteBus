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
    ///     Resolves the single main handler for a message, combining direct and indirect registrations.
    /// </summary>
    /// <typeparam name="TMessage">The message type being mediated.</typeparam>
    /// <param name="messageDependencies">The message dependencies that supply handler collections.</param>
    /// <returns>The resolved lazy handler instance.</returns>
    /// <exception cref="NoHandlerFoundException">Thrown when no handler is registered.</exception>
    /// <exception cref="MultipleHandlerFoundException">Thrown when more than one handler is registered.</exception>
    public static LazyHandler<IMessageHandler, IMainHandlerDescriptor> Resolve<TMessage>(
        IMessageDependencies messageDependencies)
        where TMessage : notnull
    {
        ArgumentNullException.ThrowIfNull(messageDependencies);

        var handlers = messageDependencies.MainHandlers
            .Concat(messageDependencies.IndirectMainHandlers)
            .ToList();

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
}
