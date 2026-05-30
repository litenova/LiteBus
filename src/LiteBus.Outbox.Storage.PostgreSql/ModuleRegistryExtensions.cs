using System;
using LiteBus.Runtime.Abstractions;

namespace LiteBus.Outbox.Storage.PostgreSql;

/// <summary>
///     Provides extension methods for registering PostgreSQL outbox stores.
/// </summary>
public static class ModuleRegistryExtensions
{
    /// <summary>
    ///     Registers the PostgreSQL outbox store.
    /// </summary>
    /// <param name="moduleRegistry">The module registry.</param>
    /// <param name="builderAction">The PostgreSQL store configuration action.</param>
    /// <returns>The current module registry.</returns>
    public static IModuleRegistry AddPostgreSqlOutboxStorage(
        this IModuleRegistry moduleRegistry,
        Action<PostgreSqlOutboxModuleBuilder> builderAction)
    {
        ArgumentNullException.ThrowIfNull(moduleRegistry);
        ArgumentNullException.ThrowIfNull(builderAction);

        moduleRegistry.Register(new PostgreSqlOutboxModule(builderAction));
        return moduleRegistry;
    }
}