using System;
using System.Linq;
using LiteBus.Outbox.Abstractions;
using LiteBus.Runtime.Abstractions;
using LiteBus.Runtime.Abstractions.Exceptions;
using LiteBus.Transport.Abstractions;

namespace LiteBus.Outbox.Dispatch.Transport;

/// <summary>
///     Module that registers <see cref="TransportOutboxDispatcher" /> and an optional transport child module.
/// </summary>
public sealed class TransportOutboxDispatchModule : IModule
{
    /// <summary>
    ///     Gets the optional transport module registered before the dispatcher.
    /// </summary>
    private readonly IModule? _transportModule;

    /// <summary>
    ///     Gets the dispatcher options configured by the application.
    /// </summary>
    private readonly TransportOutboxDispatcherOptions _options;

    /// <summary>
    ///     Initializes a new instance of the <see cref="TransportOutboxDispatchModule" /> class.
    /// </summary>
    /// <param name="options">The dispatcher options configured by the application.</param>
    /// <param name="transportModule">The optional transport module that registers <see cref="IMessageTransport" />.</param>
    public TransportOutboxDispatchModule(TransportOutboxDispatcherOptions options, IModule? transportModule = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _transportModule = transportModule;
    }

    /// <inheritdoc />
    public void Build(IModuleConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        if (!configuration.TryGetContext<OutboxCoreRegisteredMarker>(out _))
        {
            throw new LiteBusConfigurationException(
                $"{nameof(TransportOutboxDispatchModule)} requires OutboxModule core services " +
                "to be registered first. Configure the dispatcher inside AddOutboxModule(...) " +
                "using UseTransport().");
        }

        if (configuration.DependencyRegistry.Any(descriptor => descriptor.DependencyType == typeof(IOutboxDispatcher)))
        {
            throw new LiteBusConfigurationException(
                "An IOutboxDispatcher is already registered. Register only one outbox dispatcher implementation.");
        }

        _transportModule?.Build(configuration);

        if (!configuration.DependencyRegistry.Any(descriptor => descriptor.DependencyType == typeof(IMessageTransport)))
        {
            throw new LiteBusConfigurationException(
                $"{nameof(TransportOutboxDispatchModule)} requires {nameof(IMessageTransport)} to be registered. " +
                "Register a transport module through UseTransport().");
        }

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(TransportOutboxDispatcherOptions),
            _options));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IOutboxDispatcher),
            typeof(TransportOutboxDispatcher)));
    }
}
