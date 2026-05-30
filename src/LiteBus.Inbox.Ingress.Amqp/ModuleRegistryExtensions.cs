using System;
using LiteBus.Runtime.Abstractions;

namespace LiteBus.Inbox.Ingress.Amqp;

/// <summary>
///     Provides extension methods for registering AMQP inbox ingress.
/// </summary>
public static class ModuleRegistryExtensions
{
    /// <summary>
    ///     Registers AMQP inbox ingress services.
    /// </summary>
    /// <param name="moduleRegistry">The module registry.</param>
    /// <param name="configure">The AMQP ingress configuration action.</param>
    /// <returns>The current module registry.</returns>
    /// <exception cref="InvalidOperationException">
    ///     Thrown when <see cref="AmqpInboxIngressModule" /> is already registered.
    /// </exception>
    /// <remarks>
    ///     Call this after <c>AddInboxModule</c> and an inbox store registration. Pair with
    ///     <see cref="AddInboxAmqpIngressHosting" /> to run the consumer on the generic host.
    /// </remarks>
    public static IModuleRegistry AddInboxAmqpIngress(
        this IModuleRegistry moduleRegistry,
        Action<AmqpInboxIngressModuleBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(moduleRegistry);
        ArgumentNullException.ThrowIfNull(configure);

        if (moduleRegistry.IsModuleRegistered<AmqpInboxIngressModule>())
        {
            throw new InvalidOperationException(
                "The AMQP inbox ingress module is already registered. Call AddInboxAmqpIngress only once.");
        }

        moduleRegistry.Register(new AmqpInboxIngressModule(configure));
        return moduleRegistry;
    }

    /// <summary>
    ///     Registers the AMQP inbox ingress background service for the generic host.
    /// </summary>
    /// <param name="moduleRegistry">The LiteBus module registry.</param>
    /// <param name="configure">An optional callback that configures whether the ingress loop is enabled.</param>
    /// <returns>The current module registry.</returns>
    /// <remarks>
    ///     Call <see cref="AddInboxAmqpIngress" /> before calling this method.
    /// </remarks>
    public static IModuleRegistry AddInboxAmqpIngressHosting(
        this IModuleRegistry moduleRegistry,
        Action<AmqpInboxIngressHostOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(moduleRegistry);

        moduleRegistry.Register(new AmqpInboxIngressHostingModule(configure));
        return moduleRegistry;
    }

    /// <summary>
    ///     Registers AMQP inbox ingress for RabbitMQ using the shared AMQP implementation.
    /// </summary>
    /// <param name="moduleRegistry">The module registry.</param>
    /// <param name="configure">The AMQP ingress configuration action.</param>
    /// <returns>The current module registry.</returns>
    public static IModuleRegistry AddInboxRabbitMqIngress(
        this IModuleRegistry moduleRegistry,
        Action<AmqpInboxIngressModuleBuilder> configure)
    {
        return AddInboxAmqpIngress(moduleRegistry, configure);
    }

    /// <summary>
    ///     Registers AMQP inbox ingress for LavinMQ using the shared AMQP implementation.
    /// </summary>
    /// <param name="moduleRegistry">The module registry.</param>
    /// <param name="configure">The AMQP ingress configuration action.</param>
    /// <returns>The current module registry.</returns>
    public static IModuleRegistry AddInboxLavinMqIngress(
        this IModuleRegistry moduleRegistry,
        Action<AmqpInboxIngressModuleBuilder> configure)
    {
        return AddInboxAmqpIngress(moduleRegistry, configure);
    }

    /// <summary>
    ///     Registers the AMQP inbox ingress background service for RabbitMQ.
    /// </summary>
    /// <param name="moduleRegistry">The LiteBus module registry.</param>
    /// <param name="configure">An optional callback that configures whether the ingress loop is enabled.</param>
    /// <returns>The current module registry.</returns>
    public static IModuleRegistry AddInboxRabbitMqIngressHosting(
        this IModuleRegistry moduleRegistry,
        Action<AmqpInboxIngressHostOptions>? configure = null)
    {
        return AddInboxAmqpIngressHosting(moduleRegistry, configure);
    }

    /// <summary>
    ///     Registers the AMQP inbox ingress background service for LavinMQ.
    /// </summary>
    /// <param name="moduleRegistry">The LiteBus module registry.</param>
    /// <param name="configure">An optional callback that configures whether the ingress loop is enabled.</param>
    /// <returns>The current module registry.</returns>
    public static IModuleRegistry AddInboxLavinMqIngressHosting(
        this IModuleRegistry moduleRegistry,
        Action<AmqpInboxIngressHostOptions>? configure = null)
    {
        return AddInboxAmqpIngressHosting(moduleRegistry, configure);
    }
}
