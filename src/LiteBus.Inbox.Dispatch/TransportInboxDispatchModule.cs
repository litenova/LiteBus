using System;
using System.Linq;
using LiteBus.Inbox.Abstractions;
using LiteBus.Runtime.Abstractions;
using LiteBus.Runtime.Abstractions.Exceptions;
using LiteBus.Transport.Abstractions;

namespace LiteBus.Inbox.Dispatch;

/// <summary>
///     Module that registers <see cref="TransportInboxDispatcher" /> and an optional transport child module.
/// </summary>
public sealed class TransportInboxDispatchModule : IInboxDispatcherModule, IRequires<InboxModule>
{
    /// <summary>
    ///     Gets the dispatcher options configured by the application.
    /// </summary>
    private readonly TransportInboxDispatcherOptions _options;

    /// <summary>
    ///     Gets the optional transport module registered before the dispatcher.
    /// </summary>
    private readonly IModule? _transportModule;

    /// <summary>
    ///     Initializes a new instance of the <see cref="TransportInboxDispatchModule" /> class.
    /// </summary>
    /// <param name="options">The dispatcher options configured by the application.</param>
    /// <param name="transportModule">The optional transport module that registers <see cref="IMessageTransport" />.</param>
    public TransportInboxDispatchModule(TransportInboxDispatcherOptions options, IModule? transportModule = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _transportModule = transportModule;
    }

    /// <inheritdoc />
    public void Build(IModuleConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        if (configuration.DependencyRegistry.Any(descriptor => descriptor.DependencyType == typeof(IInboxDispatcher)))
        {
            throw new LiteBusConfigurationException(
                "An IInboxDispatcher is already registered. Register only one inbox dispatcher implementation.");
        }

        _transportModule?.Build(configuration);

        if (!configuration.DependencyRegistry.Any(descriptor => descriptor.DependencyType == typeof(IMessageTransport)))
        {
            throw new LiteBusConfigurationException(
                $"{nameof(TransportInboxDispatchModule)} requires {nameof(IMessageTransport)} to be registered. " +
                "Register a broker-specific dispatch extension such as UseAmqpDispatch, UseAzureServiceBusDispatch, UseAwsSqsDispatch, UseKafkaDispatch, or UseInMemoryDispatch.");
        }

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(TransportInboxDispatcherOptions),
            _options));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IInboxDispatcher),
            typeof(TransportInboxDispatcher)));
    }
}