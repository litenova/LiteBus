using System;
using LiteBus.Runtime.Abstractions;

namespace LiteBus.Inbox.Storage.PostgreSql;

/// <summary>
///     Provides extension methods for registering PostgreSQL command inbox stores.
/// </summary>
public static class ModuleRegistryExtensions
{
    /// <summary>
    ///     Registers the PostgreSQL command inbox store.
    /// </summary>
    /// <param name="moduleRegistry">The module registry.</param>
    /// <param name="builderAction">The PostgreSQL store configuration action.</param>
    /// <returns>The current module registry.</returns>
    public static IModuleRegistry AddPostgreSqlInboxStorage(
        this IModuleRegistry moduleRegistry,
        Action<PostgreSqlInboxModuleBuilder> builderAction)
    {
        ArgumentNullException.ThrowIfNull(moduleRegistry);
        ArgumentNullException.ThrowIfNull(builderAction);

        moduleRegistry.Register(new PostgreSqlInboxModule(builderAction));
        return moduleRegistry;
    }
}