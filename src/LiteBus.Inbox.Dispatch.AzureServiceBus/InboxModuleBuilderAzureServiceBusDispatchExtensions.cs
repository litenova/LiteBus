using System;
using LiteBus.Inbox.Abstractions;
using LiteBus.Transport.AzureServiceBus;

namespace LiteBus.Inbox.Dispatch.AzureServiceBus;

/// <summary>
///     Registers the Azure Service Bus inbox dispatcher through <see cref="InboxModuleBuilder" />.
/// </summary>
public static class InboxModuleBuilderAzureServiceBusDispatchExtensions
{
    /// <summary>
    ///     Registers an Azure Service Bus inbox dispatcher and the matching transport module.
    /// </summary>
    /// <param name="builder">The inbox module builder.</param>
    /// <param name="configure">The dispatcher configuration action.</param>
    /// <param name="transportOptions">The Azure Service Bus connection settings.</param>
    /// <returns>The inbox module builder for chaining.</returns>
    public static InboxModuleBuilder UseAzureServiceBusDispatch(
        this InboxModuleBuilder builder,
        Action<TransportInboxDispatcherOptions> configure,
        AzureServiceBusTransportOptions transportOptions)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);
        ArgumentNullException.ThrowIfNull(transportOptions);

        var options = new TransportInboxDispatcherOptions();
        configure(options);

        return builder.RegisterDispatcher(
            new TransportInboxDispatchModule(options, new AzureServiceBusTransportModule(transportOptions)));
    }
}