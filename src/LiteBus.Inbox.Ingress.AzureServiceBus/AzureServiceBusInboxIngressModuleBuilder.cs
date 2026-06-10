using System;
using LiteBus.Inbox.Ingress;

namespace LiteBus.Inbox.Ingress.AzureServiceBus;

/// <summary>
///     Configures services registered by <see cref="AzureServiceBusInboxIngressModule" />.
/// </summary>
public sealed class AzureServiceBusInboxIngressModuleBuilder
{
    /// <summary>
    ///     Gets the ingress options that will be registered for the Service Bus consumer.
    /// </summary>
    public AzureServiceBusInboxIngressOptions Options { get; private set; } = null!;

    /// <summary>
    ///     Gets the options for the ingress background loop.
    /// </summary>
    public TransportInboxIngressHostOptions HostOptions { get; private set; } = new();

    /// <summary>
    ///     Gets a value indicating whether <see cref="TransportInboxIngressConsumer" /> is registered.
    /// </summary>
    public bool EnableIngressConsumer { get; private set; } = true;

    /// <summary>
    ///     Disables registration of the Service Bus ingress consumer background service.
    /// </summary>
    /// <returns>The current builder.</returns>
    public AzureServiceBusInboxIngressModuleBuilder DisableIngressConsumer()
    {
        EnableIngressConsumer = false;
        return this;
    }

    /// <summary>
    ///     Replaces the Azure Service Bus inbox ingress options.
    /// </summary>
    /// <param name="options">The connection and destination settings.</param>
    /// <returns>The current builder.</returns>
    public AzureServiceBusInboxIngressModuleBuilder UseOptions(AzureServiceBusInboxIngressOptions options)
    {
        Options = options ?? throw new ArgumentNullException(nameof(options));
        return this;
    }
}
