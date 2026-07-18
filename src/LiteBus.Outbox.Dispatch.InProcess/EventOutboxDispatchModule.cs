using System;
using System.Linq;
using LiteBus.Events;
using LiteBus.Outbox;
using LiteBus.Outbox.Abstractions;
using LiteBus.Runtime.Abstractions;
using LiteBus.Runtime.Abstractions.Exceptions;

namespace LiteBus.Outbox.Dispatch.InProcess;

/// <summary>
///     Module that registers <see cref="EventOutboxDispatcher" /> as <see cref="IOutboxDispatcher" />.
/// </summary>
/// <remarks>
///     Register this module through <see cref="OutboxModuleBuilderEventDispatchExtensions.UseInProcessDispatch" />
///     inside
///     <c>AddOutboxModule</c> after <c>AddEventModule</c>. The outbox module supplies contract registration and the
///     event module supplies <c>IEventMediator</c> from <c>LiteBus.Events.Abstractions</c>.
/// </remarks>
public sealed class EventOutboxDispatchModule :
    IOutboxDispatcherModule,
    IRequires<OutboxModule>,
    IRequires<EventModule>
{
    /// <inheritdoc />
    public void Build(IModuleConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IOutboxDispatcher),
            typeof(EventOutboxDispatcher)));
    }
}
