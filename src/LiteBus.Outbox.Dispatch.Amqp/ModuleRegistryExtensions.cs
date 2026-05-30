using System;
using LiteBus.Runtime.Abstractions;

namespace LiteBus.Outbox.Dispatch.Amqp;

/// <summary>
///     Provides extension methods for registering the AMQP outbox dispatcher.
/// </summary>
public static class ModuleRegistryExtensions
{
    /// <summary>
    ///     Registers <see cref="AmqpOutboxDispatcher" /> as <see cref="Outbox.Abstractions.IOutboxDispatcher" />.
    /// </summary>
    /// <param name="moduleRegistry">The module registry.</param>
    /// <param name="configure">The callback that configures AMQP connection and routing options.</param>
    /// <returns>The current module registry.</returns>
    /// <exception cref="InvalidOperationException">
    ///     Thrown when <see cref="AmqpOutboxDispatchModule" /> is already registered.
    /// </exception>
    /// <remarks>
    ///     Call this after <c>AddOutboxModule</c>. Do not register another
    ///     <see cref="Outbox.Abstractions.IOutboxDispatcher" /> when using this extension. RabbitMQ and LavinMQ both use
    ///     this registration; only connection settings differ between brokers.
    /// </remarks>
    public static IModuleRegistry AddOutboxAmqpDispatcher(
        this IModuleRegistry moduleRegistry,
        Action<AmqpOutboxDispatcherOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(moduleRegistry);
        ArgumentNullException.ThrowIfNull(configure);

        if (moduleRegistry.IsModuleRegistered<AmqpOutboxDispatchModule>())
        {
            throw new InvalidOperationException(
                "The AMQP outbox dispatcher module is already registered. Call AddOutboxAmqpDispatcher only once.");
        }

        var options = new AmqpOutboxDispatcherOptions();
        configure(options);

        moduleRegistry.Register(new AmqpOutboxDispatchModule(options));
        return moduleRegistry;
    }

    /// <summary>
    ///     Registers <see cref="AmqpOutboxDispatcher" /> for RabbitMQ brokers.
    /// </summary>
    /// <param name="moduleRegistry">The module registry.</param>
    /// <param name="configure">The callback that configures AMQP connection and routing options.</param>
    /// <returns>The current module registry.</returns>
    /// <remarks>
    ///     This method is an alias for <see cref="AddOutboxAmqpDispatcher" />. RabbitMQ and LavinMQ share the same
    ///     AMQP 0.9.1 client implementation.
    /// </remarks>
    public static IModuleRegistry AddOutboxRabbitMqDispatcher(
        this IModuleRegistry moduleRegistry,
        Action<AmqpOutboxDispatcherOptions> configure)
    {
        return AddOutboxAmqpDispatcher(moduleRegistry, configure);
    }

    /// <summary>
    ///     Registers <see cref="AmqpOutboxDispatcher" /> for LavinMQ brokers.
    /// </summary>
    /// <param name="moduleRegistry">The module registry.</param>
    /// <param name="configure">The callback that configures AMQP connection and routing options.</param>
    /// <returns>The current module registry.</returns>
    /// <remarks>
    ///     This method is an alias for <see cref="AddOutboxAmqpDispatcher" />. LavinMQ is wire-compatible with the
    ///     same <c>RabbitMQ.Client</c> stack used for RabbitMQ.
    /// </remarks>
    public static IModuleRegistry AddOutboxLavinMqDispatcher(
        this IModuleRegistry moduleRegistry,
        Action<AmqpOutboxDispatcherOptions> configure)
    {
        return AddOutboxAmqpDispatcher(moduleRegistry, configure);
    }
}
