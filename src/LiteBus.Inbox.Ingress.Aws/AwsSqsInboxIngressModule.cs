using System;
using System.Linq;
using LiteBus.Inbox.Abstractions;
using LiteBus.Runtime.Abstractions;
using LiteBus.Runtime.Abstractions.Exceptions;
using LiteBus.Transport;
using LiteBus.Transport.Abstractions;
using LiteBus.Transport.Aws;

namespace LiteBus.Inbox.Ingress.Aws;

/// <summary>
///     Module that registers AWS SQS inbox ingress services.
/// </summary>
public sealed class AwsSqsInboxIngressModule : IInboxIngressModule
{
    /// <summary>
    ///     The module builder action supplied at registration time.
    /// </summary>
    private readonly Action<AwsSqsInboxIngressModuleBuilder> _builder;

    /// <summary>
    ///     Initializes a new instance of the <see cref="AwsSqsInboxIngressModule" /> class.
    /// </summary>
    /// <param name="builder">The module configuration action.</param>
    public AwsSqsInboxIngressModule(Action<AwsSqsInboxIngressModuleBuilder> builder)
    {
        _builder = builder ?? throw new ArgumentNullException(nameof(builder));
    }

    /// <inheritdoc />
    public void Build(IModuleConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var moduleBuilder = new AwsSqsInboxIngressModuleBuilder();
        _builder(moduleBuilder);

        var options = moduleBuilder.Options ??
                      throw new LiteBusConfigurationException(
                          $"{nameof(AwsSqsInboxIngressOptions)} must be configured before registering AWS SQS inbox ingress.");

        if (string.IsNullOrWhiteSpace(options.Destination))
        {
            throw new LiteBusConfigurationException(
                $"{nameof(AwsSqsInboxIngressOptions.Destination)} must be configured before registering AWS SQS inbox ingress.");
        }

        EnsureTransportRegistered(configuration, options);

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
    ///     Ensures <see cref="IMessageConsumer" /> is registered, bootstrapping SQS transport from ingress options when
    ///     needed.
    /// </summary>
    /// <param name="configuration">The module configuration receiving dependency registrations.</param>
    /// <param name="options">The ingress options supplying connection settings when transport is not pre-registered.</param>
    private static void EnsureTransportRegistered(IModuleConfiguration configuration, AwsSqsInboxIngressOptions options)
    {
        if (configuration.DependencyRegistry.Any(descriptor => descriptor.DependencyType == typeof(IMessageConsumer)))
        {
            return;
        }

        new AwsSqsTransportModule(options.Connection).Build(configuration);

        if (configuration.DependencyRegistry.Any(descriptor => descriptor.DependencyType == typeof(IMessageConsumer)))
        {
            return;
        }

        throw new LiteBusConfigurationException(
            "AWS SQS inbox ingress requires IMessageConsumer to be registered. " +
            "Configure AwsSqsInboxIngressOptions.Connection or register AwsSqsTransportModule before calling UseAwsSqsIngress().");
    }
}