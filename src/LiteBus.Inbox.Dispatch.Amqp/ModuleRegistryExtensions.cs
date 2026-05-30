using System;
using LiteBus.Runtime.Abstractions;

namespace LiteBus.Inbox.Dispatch.Amqp;

/// <summary>
///     Provides extension methods for registering the AMQP inbox dispatcher.
/// </summary>
public static class ModuleRegistryExtensions
{
    /// <summary>
    ///     Registers <see cref="AmqpInboxDispatcher" /> as <see cref="Inbox.Abstractions.IInboxDispatcher" />.
    /// </summary>
    /// <param name="moduleRegistry">The module registry.</param>
    /// <param name="configure">The callback that configures AMQP connection and routing options.</param>
    /// <returns>The current module registry.</returns>
    /// <exception cref="InvalidOperationException">
    ///     Thrown when <see cref="AmqpInboxDispatchModule" /> is already registered.
    /// </exception>
    /// <remarks>
    ///     Call this after <c>AddInboxModule</c>. Do not register another
    ///     <see cref="Inbox.Abstractions.IInboxDispatcher" /> when using this extension. RabbitMQ and LavinMQ both use
    ///     this registration; only connection settings differ between brokers.
    /// </remarks>
    public static IModuleRegistry AddInboxAmqpDispatcher(
        this IModuleRegistry moduleRegistry,
        Action<AmqpInboxDispatcherOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(moduleRegistry);
        ArgumentNullException.ThrowIfNull(configure);

        if (moduleRegistry.IsModuleRegistered<AmqpInboxDispatchModule>())
        {
            throw new InvalidOperationException(
                "The AMQP inbox dispatcher module is already registered. Call AddInboxAmqpDispatcher only once.");
        }

        var options = new AmqpInboxDispatcherOptions();
        configure(options);

        moduleRegistry.Register(new AmqpInboxDispatchModule(options));
        return moduleRegistry;
    }

    /// <summary>
    ///     Registers <see cref="AmqpInboxDispatcher" /> for RabbitMQ brokers.
    /// </summary>
    /// <param name="moduleRegistry">The module registry.</param>
    /// <param name="configure">The callback that configures AMQP connection and routing options.</param>
    /// <returns>The current module registry.</returns>
    /// <remarks>
    ///     This method is an alias for <see cref="AddInboxAmqpDispatcher" />. RabbitMQ and LavinMQ share the same
    ///     AMQP 0.9.1 client implementation.
    /// </remarks>
    public static IModuleRegistry AddInboxRabbitMqDispatcher(
        this IModuleRegistry moduleRegistry,
        Action<AmqpInboxDispatcherOptions> configure)
    {
        return AddInboxAmqpDispatcher(moduleRegistry, configure);
    }

    /// <summary>
    ///     Registers <see cref="AmqpInboxDispatcher" /> for LavinMQ brokers.
    /// </summary>
    /// <param name="moduleRegistry">The module registry.</param>
    /// <param name="configure">The callback that configures AMQP connection and routing options.</param>
    /// <returns>The current module registry.</returns>
    /// <remarks>
    ///     This method is an alias for <see cref="AddInboxAmqpDispatcher" />. LavinMQ is wire-compatible with the
    ///     same <c>RabbitMQ.Client</c> stack used for RabbitMQ.
    /// </remarks>
    public static IModuleRegistry AddInboxLavinMqDispatcher(
        this IModuleRegistry moduleRegistry,
        Action<AmqpInboxDispatcherOptions> configure)
    {
        return AddInboxAmqpDispatcher(moduleRegistry, configure);
    }
}
