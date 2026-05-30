using System;
using LiteBus.Runtime.Abstractions;

namespace LiteBus.Inbox.Storage.InMemory;

/// <summary>
///     Provides extension methods for registering the in-memory command inbox store.
/// </summary>
public static class ModuleRegistryExtensions
{
    /// <summary>
    ///     Registers the in-memory command inbox store with default options.
    /// </summary>
    /// <param name="moduleRegistry">The module registry.</param>
    /// <returns>The current module registry.</returns>
    public static IModuleRegistry AddInMemoryInboxStorage(this IModuleRegistry moduleRegistry)
    {
        return AddInMemoryInboxStorage(moduleRegistry, _ =>
        {
        });
    }

    /// <summary>
    ///     Registers the in-memory command inbox store.
    /// </summary>
    /// <param name="moduleRegistry">The module registry.</param>
    /// <param name="builderAction">The in-memory store configuration action.</param>
    /// <returns>The current module registry.</returns>
    public static IModuleRegistry AddInMemoryInboxStorage(
        this IModuleRegistry moduleRegistry,
        Action<InMemoryInboxStorageModuleBuilder> builderAction)
    {
        ArgumentNullException.ThrowIfNull(moduleRegistry);
        ArgumentNullException.ThrowIfNull(builderAction);

        moduleRegistry.Register(new InMemoryInboxStorageModule(builderAction));
        return moduleRegistry;
    }
}
