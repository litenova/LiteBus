using System;
using System.ComponentModel;
using LiteBus.Messaging.Abstractions;
using LiteBus.Runtime.Abstractions;

namespace LiteBus.Messaging.Mediator;

/// <summary>
///     Builds the core mediator without a container.
/// </summary>
/// <remarks>
///     <para>
///         The messaging module registers <see cref="IMessageMediator" /> by type and lets the container build it,
///         which is the path every host takes. This exists for the two callers that have no container: a test harness
///         running the shipped pipeline over hand-supplied handlers, and a manual host composing LiteBus by hand.
///     </para>
///     <para>
///         Nothing an application writes should need it. It is public because <c>MessageMediator</c> is internal and
///         those callers live in other assemblies.
///     </para>
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class MessageMediatorFactory
{
    /// <summary>
    ///     Creates a mediator over one registry and one dispatch scope factory.
    /// </summary>
    /// <param name="registry">The registry holding the messages and handlers to mediate.</param>
    /// <param name="dispatchScopeFactory">The factory that creates the per-mediation scope handlers resolve from.</param>
    /// <returns>The mediator.</returns>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="registry" /> or <paramref name="dispatchScopeFactory" /> is <see langword="null" />.
    /// </exception>
    public static IMessageMediator Create(
        IMessageRegistry registry,
        IMessageDispatchScopeFactory dispatchScopeFactory)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(dispatchScopeFactory);

        return new MessageMediator(registry, registry, dispatchScopeFactory);
    }
}
