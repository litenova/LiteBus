using System;
using LiteBus.Runtime.Abstractions;

namespace LiteBus.Outbox.PostgreSql.Extensions.Microsoft.Hosting;

/// <summary>
///     Provides Microsoft hosting registration extensions for PostgreSQL outbox schema bootstrap.
/// </summary>
public static class ModuleRegistryHostingExtensions
{
    /// <summary>
    ///     Registers a hosted service that creates or upgrades the PostgreSQL outbox schema on startup.
    /// </summary>
    /// <param name="moduleRegistry">The LiteBus module registry.</param>
    /// <returns>The current module registry.</returns>
    /// <remarks>
    ///     <para>
    ///         Call <see cref="Outbox.PostgreSql.ModuleRegistryExtensions.AddPostgreSqlOutboxStore" /> first and set
    ///         <see cref="PostgreSqlOutboxStoreOptions.EnsureSchemaCreationOnStartup" /> to <see langword="true" /> or call
    ///         <see cref="PostgreSqlOutboxModuleBuilder.EnsureSchemaCreationOnStartup" /> on the store builder.
    ///     </para>
    ///     <para>
    ///         Register this module before `AddOutboxProcessorHosting` (from
    ///         `LiteBus.Outbox.Extensions.Microsoft.Hosting`) so schema bootstrap completes before the processor loop
    ///         starts.
    ///     </para>
    /// </remarks>
    public static IModuleRegistry AddPostgreSqlOutboxSchemaHosting(this IModuleRegistry moduleRegistry)
    {
        ArgumentNullException.ThrowIfNull(moduleRegistry);

        moduleRegistry.Register(new PostgreSqlOutboxSchemaHostingModule());
        return moduleRegistry;
    }
}
