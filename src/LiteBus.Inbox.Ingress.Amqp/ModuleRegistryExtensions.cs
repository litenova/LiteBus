using System;
using LiteBus.Runtime.Abstractions;

namespace LiteBus.Inbox.Ingress.Amqp;

/// <summary>
///     Provides extension methods for registering AMQP inbox ingress.
/// </summary>
public static class ModuleRegistryExtensions
{
    /// <summary>
    ///     Registers AMQP inbox ingress services and background work for the generic host.
    /// </summary>
    /// <param name="moduleRegistry">The module registry.</param>
    /// <param name="configure">The AMQP ingress configuration action.</param>
    /// <returns>The current module registry.</returns>
    /// <exception cref="InvalidOperationException">
    ///     Thrown when <see cref="AmqpInboxIngressModule" /> is already registered.
    /// </exception>
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
}
