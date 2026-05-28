using System;
using LiteBus.Runtime.Abstractions;

namespace LiteBus.Inbox.PostgreSql;

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
    public static IModuleRegistry AddPostgreSqlCommandInboxStore(
        this IModuleRegistry moduleRegistry,
        Action<PostgreSqlCommandInboxModuleBuilder> builderAction)
    {
        ArgumentNullException.ThrowIfNull(moduleRegistry);
        ArgumentNullException.ThrowIfNull(builderAction);

        moduleRegistry.Register(new PostgreSqlCommandInboxModule(builderAction));
        return moduleRegistry;
    }
}