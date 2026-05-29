using System;
using LiteBus.Messaging;
using LiteBus.Runtime.Abstractions;

namespace LiteBus.Inbox;

/// <summary>
///     Provides extension methods for registering command inbox modules.
/// </summary>
public static class ModuleRegistryExtensions
{
    /// <summary>
    ///     Registers the command inbox module.
    /// </summary>
    /// <param name="moduleRegistry">The module registry.</param>
    /// <param name="builderAction">The command inbox module configuration action.</param>
    /// <returns>The current module registry.</returns>
    public static IModuleRegistry AddCommandInboxModule(this IModuleRegistry moduleRegistry, Action<CommandInboxModuleBuilder> builderAction)
    {
        ArgumentNullException.ThrowIfNull(moduleRegistry);
        ArgumentNullException.ThrowIfNull(builderAction);

        if (!moduleRegistry.IsModuleRegistered<MessageModule>())
        {
            moduleRegistry.Register(new MessageModule(moduleBuilder =>
            {
            }));
        }

        moduleRegistry.Register(new CommandInboxModule(builderAction));
        return moduleRegistry;
    }

    /// <summary>
    ///     Registers the command inbox module with default settings.
    /// </summary>
    /// <param name="moduleRegistry">The module registry.</param>
    /// <returns>The current module registry.</returns>
    public static IModuleRegistry AddCommandInboxModule(this IModuleRegistry moduleRegistry)
    {
        return AddCommandInboxModule(moduleRegistry, moduleBuilder =>
        {
        });
    }
}