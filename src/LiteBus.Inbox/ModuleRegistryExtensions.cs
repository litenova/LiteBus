using System;
using LiteBus.Messaging;
using LiteBus.Runtime.Abstractions;

namespace LiteBus.Inbox;

/// <summary>
///     Provides extension methods for registering inbox modules.
/// </summary>
public static class ModuleRegistryExtensions
{
    /// <summary>
    ///     Registers the inbox module.
    /// </summary>
    /// <param name="moduleRegistry">The module registry.</param>
    /// <param name="builderAction">The inbox module configuration action.</param>
    /// <returns>The current module registry.</returns>
    public static IModuleRegistry AddInboxModule(this IModuleRegistry moduleRegistry, Action<InboxModuleBuilder> builderAction)
    {
        ArgumentNullException.ThrowIfNull(moduleRegistry);
        ArgumentNullException.ThrowIfNull(builderAction);

        if (!moduleRegistry.IsModuleRegistered<MessageModule>())
        {
            moduleRegistry.Register(new MessageModule(moduleBuilder =>
            {
            }));
        }

        moduleRegistry.Register(new InboxModule(builderAction));
        return moduleRegistry;
    }

    /// <summary>
    ///     Registers the inbox module with default settings.
    /// </summary>
    /// <param name="moduleRegistry">The module registry.</param>
    /// <returns>The current module registry.</returns>
    public static IModuleRegistry AddInboxModule(this IModuleRegistry moduleRegistry)
    {
        return AddInboxModule(moduleRegistry, moduleBuilder =>
        {
        });
    }
}
