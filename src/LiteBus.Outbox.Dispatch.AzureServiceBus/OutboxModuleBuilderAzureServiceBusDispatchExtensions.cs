using System;
using LiteBus.Outbox.Abstractions;
using LiteBus.Transport.AzureServiceBus;

namespace LiteBus.Outbox.Dispatch.AzureServiceBus;

/// <summary>
///     Registers the Azure Service Bus outbox dispatcher through <see cref="OutboxModuleBuilder" />.
/// </summary>
public static class OutboxModuleBuilderAzureServiceBusDispatchExtensions
{
    /// <summary>
    ///     Registers an Azure Service Bus outbox dispatcher and the matching transport module.
    /// </summary>
    /// <param name="builder">The outbox module builder.</param>
    /// <param name="configure">The dispatcher configuration action.</param>
    /// <param name="transportOptions">The Azure Service Bus connection settings.</param>
    /// <returns>The outbox module builder for chaining.</returns>
    public static OutboxModuleBuilder UseAzureServiceBusDispatch(
        this OutboxModuleBuilder builder,
        Action<TransportOutboxDispatcherOptions> configure,
        AzureServiceBusTransportOptions transportOptions)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);
        ArgumentNullException.ThrowIfNull(transportOptions);

        var options = new TransportOutboxDispatcherOptions();
        configure(options);

        return builder.RegisterDispatcher(
            new TransportOutboxDispatchModule<AzureServiceBusTransportModule>(
                options,
                new AzureServiceBusTransportModule(transportOptions)));
    }

    /// <summary>
    ///     Registers an Azure Service Bus outbox dispatcher that uses an <see cref="AzureServiceBusTransportModule" />
    ///     registered elsewhere in the module graph.
    /// </summary>
    /// <param name="builder">The outbox module builder.</param>
    /// <param name="configure">The dispatcher configuration action.</param>
    /// <returns>The outbox module builder for chaining.</returns>
    public static OutboxModuleBuilder UseAzureServiceBusDispatchWithRegisteredTransport(
        this OutboxModuleBuilder builder,
        Action<TransportOutboxDispatcherOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new TransportOutboxDispatcherOptions();
        configure(options);
        return builder.RegisterDispatcher(
            new TransportOutboxDispatchModule<AzureServiceBusTransportModule>(options));
    }
}
