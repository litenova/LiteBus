using System;
using LiteBus.Runtime.Abstractions;

namespace LiteBus.Inbox.Storage.PostgreSql.Extensions.Microsoft.Hosting;

/// <summary>
///     Provides Microsoft hosting registration extensions for PostgreSQL command inbox schema bootstrap.
/// </summary>
public static class ModuleRegistryHostingExtensions
{
    /// <summary>
    ///     Registers a hosted service that creates or upgrades the PostgreSQL command inbox schema on startup.
    /// </summary>
    /// <param name="moduleRegistry">The LiteBus module registry.</param>
    /// <returns>The current module registry.</returns>
    /// <remarks>
    ///     <para>
    ///         Call <see cref="Inbox.Storage.PostgreSql.ModuleRegistryExtensions.AddPostgreSqlInboxStorage" /> first and set
    ///         <see cref="PostgreSqlInboxStoreOptions.EnsureSchemaCreationOnStartup" /> to <see langword="true" /> or call
    ///         <see cref="PostgreSqlInboxModuleBuilder.EnsureSchemaCreationOnStartup" /> on the store builder.
    ///     </para>
    ///     <para>
    ///         Register this module before `AddInboxProcessorHosting` (from
    ///         `LiteBus.Inbox.Extensions.Microsoft.Hosting`) so schema bootstrap completes before the processor loop
    ///         starts.
    ///     </para>
    /// </remarks>
    public static IModuleRegistry AddPostgreSqlInboxStorageSchemaHosting(this IModuleRegistry moduleRegistry)
    {
        ArgumentNullException.ThrowIfNull(moduleRegistry);

        moduleRegistry.Register(new PostgreSqlInboxSchemaHostingModule());
        return moduleRegistry;
    }
}
