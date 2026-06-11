using System;
using System.Linq;
using LiteBus.Outbox.Abstractions;
using LiteBus.Runtime.Abstractions;
using LiteBus.Runtime.Abstractions.Exceptions;

namespace LiteBus.Outbox.Dispatch.InProcess;

/// <summary>
///     Module that registers <see cref="EventOutboxDispatcher" /> as <see cref="IOutboxDispatcher" />.
/// </summary>
/// <remarks>
///     Register this module through <see cref="OutboxModuleBuilderEventDispatchExtensions.UseEventOutboxDispatcher" />
///     inside
///     <c>AddOutboxModule</c> after <c>AddEventModule</c>. The outbox module supplies contract registration and the
///     event module supplies <c>IEventMediator</c> from <c>LiteBus.Events.Abstractions</c>.
/// </remarks>
public sealed class EventOutboxDispatchModule : IOutboxDispatcherModule, IRequires<OutboxModule>
{
    /// <inheritdoc />
    public void Build(IModuleConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        if (configuration.DependencyRegistry.Any(descriptor => descriptor.DependencyType == typeof(IOutboxDispatcher)))
        {
            throw new LiteBusConfigurationException(
                "An IOutboxDispatcher is already registered. Register only one outbox dispatcher implementation.");
        }

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IOutboxDispatcher),
            typeof(EventOutboxDispatcher)));
    }
}