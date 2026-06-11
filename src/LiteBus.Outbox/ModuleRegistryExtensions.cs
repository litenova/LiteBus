using System;
using LiteBus.Messaging;
using LiteBus.Outbox.Abstractions;
using LiteBus.Runtime.Abstractions;
using LiteBus.Runtime.Abstractions.Exceptions;

namespace LiteBus.Outbox;

/// <summary>
///     Provides extension methods for registering outbox modules.
/// </summary>
public static class ModuleRegistryExtensions
{
    /// <summary>
    ///     Registers the outbox module.
    /// </summary>
    /// <param name="moduleRegistry">The module registry.</param>
    /// <param name="builderAction">The outbox module configuration action.</param>
    /// <returns>The current module registry.</returns>
    /// <exception cref="LiteBusConfigurationException">
    ///     Thrown when <see cref="MessageModule" /> has not been registered.
    /// </exception>
    public static IModuleRegistry AddOutboxModule(this IModuleRegistry moduleRegistry, Action<OutboxModuleBuilder> builderAction)
    {
        ArgumentNullException.ThrowIfNull(moduleRegistry);
        ArgumentNullException.ThrowIfNull(builderAction);

        if (!moduleRegistry.IsModuleRegistered<MessageModule>())
        {
            throw new LiteBusConfigurationException(
                "MessageModule must be registered before AddOutboxModule(). " +
                "Call AddMessageModule() first, or register a command, event, or query module after AddMessageModule().");
        }

        moduleRegistry.Register(new OutboxModule(builderAction));
        return moduleRegistry;
    }

    /// <summary>
    ///     Registers the outbox module with default settings.
    /// </summary>
    /// <param name="moduleRegistry">The module registry.</param>
    /// <returns>The current module registry.</returns>
    /// <exception cref="LiteBusConfigurationException">
    ///     Thrown when <see cref="MessageModule" /> has not been registered.
    /// </exception>
    public static IModuleRegistry AddOutboxModule(this IModuleRegistry moduleRegistry)
    {
        return moduleRegistry.AddOutboxModule(_ =>
        {
        });
    }
}