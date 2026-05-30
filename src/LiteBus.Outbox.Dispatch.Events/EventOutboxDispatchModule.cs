using System;
using System.Linq;
using LiteBus.Outbox.Abstractions;
using LiteBus.Runtime.Abstractions;

namespace LiteBus.Outbox.Dispatch.Events;

/// <summary>
///     Module that registers <see cref="EventOutboxDispatcher" /> as <see cref="IOutboxDispatcher" />.
/// </summary>
/// <remarks>
///     Register this module through <see cref="ModuleRegistryExtensions.AddOutboxEventDispatcher" /> after
///     <c>AddOutboxModule</c> and <c>AddEventModule</c>. The outbox module supplies contract registration and the
///     event module supplies <c>IEventPublisher</c> from <c>LiteBus.Events.Abstractions</c>.
/// </remarks>
public sealed class EventOutboxDispatchModule : IModule
{
    /// <inheritdoc />
    public void Build(IModuleConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        if (configuration.DependencyRegistry.Any(descriptor => descriptor.DependencyType == typeof(IOutboxDispatcher)))
        {
            throw new InvalidOperationException(
                "An IOutboxDispatcher is already registered. Register only one outbox dispatcher implementation.");
        }

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IOutboxDispatcher),
            typeof(EventOutboxDispatcher)));
    }
}
