using System;
using LiteBus.Inbox.Abstractions;

namespace LiteBus.Inbox.Ingress.AzureServiceBus;

/// <summary>
///     Registers Azure Service Bus inbox ingress through <see cref="InboxModuleBuilder" />.
/// </summary>
public static class InboxModuleBuilderAzureServiceBusIngressExtensions
{
    /// <summary>
    ///     Registers Azure Service Bus inbox ingress as an inbox child module.
    /// </summary>
    /// <param name="builder">The inbox module builder.</param>
    /// <param name="configure">The Service Bus ingress configuration action.</param>
    /// <returns>The inbox module builder for chaining.</returns>
    public static InboxModuleBuilder UseAzureServiceBusIngress(
        this InboxModuleBuilder builder,
        Action<AzureServiceBusInboxIngressModuleBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);
        return builder.RegisterIngress(new AzureServiceBusInboxIngressModule(configure));
    }
}