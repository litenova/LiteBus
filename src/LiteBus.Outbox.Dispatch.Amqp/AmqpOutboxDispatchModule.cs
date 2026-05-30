using System;
using System.Linq;
using LiteBus.Amqp;
using LiteBus.Outbox.Abstractions;
using LiteBus.Runtime.Abstractions;

namespace LiteBus.Outbox.Dispatch.Amqp;

/// <summary>
///     Module that registers <see cref="AmqpOutboxDispatcher" /> and the AMQP services it depends on.
/// </summary>
/// <remarks>
///     Register this module through <see cref="ModuleRegistryExtensions.AddOutboxAmqpDispatcher" /> after
///     <c>AddOutboxModule</c>. The outbox module supplies contract registration and the messaging module supplies
///     <see cref="Messaging.Abstractions.IMessageSerializer" />.
/// </remarks>
public sealed class AmqpOutboxDispatchModule : IModule
{
    /// <summary>
    ///     Gets the dispatcher options configured by the application.
    /// </summary>
    private readonly AmqpOutboxDispatcherOptions _options;

    /// <summary>
    ///     Initializes a new instance of the <see cref="AmqpOutboxDispatchModule" /> class.
    /// </summary>
    /// <param name="options">The dispatcher options configured by the application.</param>
    public AmqpOutboxDispatchModule(AmqpOutboxDispatcherOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

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
            typeof(AmqpOutboxDispatcherOptions),
            _options));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(AmqpConnectionOptions),
            _options.Connection));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IAmqpConnectionManager),
            typeof(AmqpConnectionManager)));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IAmqpPublisher),
            typeof(AmqpPublisher)));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IOutboxDispatcher),
            typeof(AmqpOutboxDispatcher)));
    }
}
