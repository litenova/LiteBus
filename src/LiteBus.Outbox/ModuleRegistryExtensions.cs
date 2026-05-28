using System;
using LiteBus.Messaging;
using LiteBus.Runtime.Abstractions;

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
    public static IModuleRegistry AddOutboxModule(this IModuleRegistry moduleRegistry, Action<OutboxModuleBuilder> builderAction)
    {
        ArgumentNullException.ThrowIfNull(moduleRegistry);
        ArgumentNullException.ThrowIfNull(builderAction);

        if (!moduleRegistry.IsModuleRegistered<MessageModule>())
        {
            moduleRegistry.Register(new MessageModule(moduleBuilder =>
            {
            }));
        }

        moduleRegistry.Register(new OutboxModule(builderAction));
        return moduleRegistry;
    }

    /// <summary>
    ///     Registers the outbox module with default settings.
    /// </summary>
    /// <param name="moduleRegistry">The module registry.</param>
    /// <returns>The current module registry.</returns>
    public static IModuleRegistry AddOutboxModule(this IModuleRegistry moduleRegistry)
    {
        return AddOutboxModule(moduleRegistry, moduleBuilder =>
        {
        });
    }
}