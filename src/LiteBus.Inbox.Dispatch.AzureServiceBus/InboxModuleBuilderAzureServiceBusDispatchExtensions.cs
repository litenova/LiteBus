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
    ///     Registers an Azure Service Bus inbox dispatcher that uses an <see cref="AzureServiceBusTransportModule" />
    ///     registered at the root of the module graph.
    /// </summary>
    /// <param name="builder">The inbox module builder.</param>
    /// <param name="configure">The dispatcher configuration action.</param>
    /// <returns>The inbox module builder for chaining.</returns>
    public static InboxModuleBuilder UseAzureServiceBusDispatch(
        this InboxModuleBuilder builder,
        Action<TransportInboxDispatcherOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new TransportInboxDispatcherOptions();
        configure(options);
        return builder.RegisterDispatcher(
            new TransportInboxDispatchModule<AzureServiceBusTransportModule>(options));
    }
}
