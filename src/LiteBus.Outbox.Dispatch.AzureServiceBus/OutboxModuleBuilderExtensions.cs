using System;
using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.Dispatch;
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
            new TransportOutboxDispatchModule(options, new AzureServiceBusTransportModule(transportOptions)));
    }
}
