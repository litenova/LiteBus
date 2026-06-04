using System;
using LiteBus.Runtime.Abstractions;

namespace LiteBus.Outbox.Storage.EntityFrameworkCore;

/// <summary>
///     Provides extension methods for registering Entity Framework Core outbox storage.
/// </summary>
public static class ModuleRegistryExtensions
{
    /// <summary>
    ///     Registers the Entity Framework Core outbox store.
    /// </summary>
    /// <param name="moduleRegistry">The module registry.</param>
    /// <param name="builderAction">The Entity Framework Core storage configuration action.</param>
    /// <returns>The current module registry.</returns>
    [Obsolete(
        "Use AddOutboxModule(o => o.UseEfCoreStorage(...)) instead. " +
        "This top-level registration method will be removed in a future version.")]
    public static IModuleRegistry AddEfCoreOutboxStorage(
        this IModuleRegistry moduleRegistry,
        Action<EfCoreOutboxStorageModuleBuilder> builderAction)
    {
        ArgumentNullException.ThrowIfNull(moduleRegistry);
        ArgumentNullException.ThrowIfNull(builderAction);

        moduleRegistry.Register(new EfCoreOutboxStorageModule(builderAction));
        return moduleRegistry;
    }
}
