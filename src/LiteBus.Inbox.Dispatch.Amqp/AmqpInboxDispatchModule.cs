using System;
using System.Linq;
using LiteBus.Amqp;
using LiteBus.Inbox.Abstractions;
using LiteBus.Runtime.Abstractions;

namespace LiteBus.Inbox.Dispatch.Amqp;

/// <summary>
///     Module that registers <see cref="AmqpInboxDispatcher" /> and the AMQP services it depends on.
/// </summary>
/// <remarks>
///     Register this module through <see cref="ModuleRegistryExtensions.AddInboxAmqpDispatcher" /> after
///     <c>AddInboxModule</c>. The inbox module supplies contract registration and the messaging module supplies
///     <see cref="Messaging.Abstractions.IMessageSerializer" />.
/// </remarks>
public sealed class AmqpInboxDispatchModule : IModule
{
    /// <summary>
    ///     Gets the dispatcher options configured by the application.
    /// </summary>
    private readonly AmqpInboxDispatcherOptions _options;

    /// <summary>
    ///     Initializes a new instance of the <see cref="AmqpInboxDispatchModule" /> class.
    /// </summary>
    /// <param name="options">The dispatcher options configured by the application.</param>
    public AmqpInboxDispatchModule(AmqpInboxDispatcherOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <inheritdoc />
    public void Build(IModuleConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        if (configuration.DependencyRegistry.Any(descriptor => descriptor.DependencyType == typeof(IInboxDispatcher)))
        {
            throw new InvalidOperationException(
                "An IInboxDispatcher is already registered. Register only one inbox dispatcher implementation.");
        }

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(AmqpInboxDispatcherOptions),
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
            typeof(IInboxDispatcher),
            typeof(AmqpInboxDispatcher)));
    }
}
