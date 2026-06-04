using System;
using LiteBus.Runtime.Abstractions;

namespace LiteBus.Inbox.Storage.PostgreSql;

/// <summary>
///     Provides extension methods for registering PostgreSQL inbox stores.
/// </summary>
public static class ModuleRegistryExtensions
{
    /// <summary>
    ///     Registers the PostgreSQL inbox store.
    /// </summary>
    /// <param name="moduleRegistry">The module registry.</param>
    /// <param name="builderAction">The PostgreSQL store configuration action.</param>
    /// <returns>The current module registry.</returns>
    [Obsolete(
        "Use AddInboxModule(i => i.UsePostgreSqlStorage(...)) instead. " +
        "This top-level registration method will be removed in a future version.")]
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