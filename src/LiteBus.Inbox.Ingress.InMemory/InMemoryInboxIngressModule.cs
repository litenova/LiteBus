using System;
using System.Linq;
using LiteBus.Inbox.Abstractions;
using LiteBus.Runtime.Abstractions;
using LiteBus.Runtime.Abstractions.Exceptions;
using LiteBus.Transport;
using LiteBus.Transport.Abstractions;
using LiteBus.Transport.InMemory;

namespace LiteBus.Inbox.Ingress.InMemory;

/// <summary>
///     Module that registers in-memory inbox ingress services.
/// </summary>
public sealed class InMemoryInboxIngressModule : IInboxIngressModule
{
    /// <summary>
    ///     The module builder action supplied at registration time.
    /// </summary>
    private readonly Action<InMemoryInboxIngressModuleBuilder> _builder;

    /// <summary>
    ///     Initializes a new instance of the <see cref="InMemoryInboxIngressModule" /> class.
    /// </summary>
    /// <param name="builder">The module configuration action.</param>
    public InMemoryInboxIngressModule(Action<InMemoryInboxIngressModuleBuilder> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        _builder = builder;
    }

    /// <inheritdoc />
    public void Build(IModuleConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var moduleBuilder = new InMemoryInboxIngressModuleBuilder();
        _builder(moduleBuilder);

        var options = moduleBuilder.Options;

        if (string.IsNullOrWhiteSpace(options.Destination))
        {
            throw new LiteBusConfigurationException(
                $"{nameof(InMemoryInboxIngressOptions.Destination)} must be configured before registering in-memory inbox ingress.");
        }

        EnsureTransportRegistered(configuration);

        var ingressOptions = new TransportInboxIngressOptions
        {
            Destination = options.Destination,
            PrefetchCount = options.PrefetchCount,
            RequeueOnFailure = options.RequeueOnFailure
        };

        configuration.DependencyRegistry.Register(new DependencyDescriptor(typeof(TransportInboxIngressOptions), ingressOptions));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(TransportInboxIngressHandler),
            typeof(TransportInboxIngressHandler)));

        if (moduleBuilder.EnableIngressConsumer)
        {
            configuration.DependencyRegistry.Register(new DependencyDescriptor(
                typeof(TransportInboxIngressHostOptions),
                moduleBuilder.HostOptions));

            configuration.DependencyRegistry.Register(new DependencyDescriptor(
                typeof(TransportInboxIngressConsumer),
                typeof(TransportInboxIngressConsumer)));

            configuration.RegisterBackgroundService(typeof(TransportInboxIngressConsumer));
        }

        TransportMetricsRegistration.RegisterIfNeeded(configuration);
    }

    /// <summary>
    ///     Ensures <see cref="IMessageConsumer" /> is registered, bootstrapping in-memory transport when needed.
    /// </summary>
    /// <param name="configuration">The module configuration receiving dependency registrations.</param>
    private static void EnsureTransportRegistered(IModuleConfiguration configuration)
    {
        if (configuration.DependencyRegistry.Any(descriptor => descriptor.DependencyType == typeof(IMessageConsumer)))
        {
            return;
        }

        new InMemoryTransportModule().Build(configuration);

        if (configuration.DependencyRegistry.Any(descriptor => descriptor.DependencyType == typeof(IMessageConsumer)))
        {
            return;
        }

        throw new LiteBusConfigurationException(
            "In-memory inbox ingress requires IMessageConsumer to be registered. " +
            "Register InMemoryTransportModule before calling UseInMemoryIngress().");
    }
}