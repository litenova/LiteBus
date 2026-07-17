using System;
using LiteBus.Runtime.Abstractions;

namespace LiteBus.Transport.AzureServiceBus;

/// <summary>
///     Adds the shared Azure Service Bus transport at the root LiteBus composition boundary.
/// </summary>
public static class LiteBusBuilderAzureServiceBusTransportExtensions
{
    /// <summary>
    ///     Registers one Azure Service Bus transport for dispatch and ingress modules to share.
    /// </summary>
    /// <param name="builder">The root LiteBus builder.</param>
    /// <param name="options">The Azure Service Bus connection settings.</param>
    /// <returns>The root builder for chaining.</returns>
    public static ILiteBusBuilder AddAzureServiceBusTransport(
        this ILiteBusBuilder builder,
        AzureServiceBusTransportOptions options)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(options);
        builder.Modules.Register(new AzureServiceBusTransportModule(options));
        return builder;
    }
}
