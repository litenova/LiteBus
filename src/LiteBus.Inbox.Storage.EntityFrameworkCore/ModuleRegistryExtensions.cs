using System;
using LiteBus.Runtime.Abstractions;

namespace LiteBus.Inbox.Storage.EntityFrameworkCore;

/// <summary>
///     Provides extension methods for registering Entity Framework Core inbox storage.
/// </summary>
public static class ModuleRegistryExtensions
{
    /// <summary>
    ///     Registers the Entity Framework Core inbox store.
    /// </summary>
    /// <param name="moduleRegistry">The module registry.</param>
    /// <param name="builderAction">The Entity Framework Core storage configuration action.</param>
    /// <returns>The current module registry.</returns>
    public static IModuleRegistry AddEfCoreInboxStorage(
        this IModuleRegistry moduleRegistry,
        Action<EfCoreInboxStorageModuleBuilder> builderAction)
    {
        ArgumentNullException.ThrowIfNull(moduleRegistry);
        ArgumentNullException.ThrowIfNull(builderAction);

        moduleRegistry.Register(new EfCoreInboxStorageModule(builderAction));
        return moduleRegistry;
    }
}
