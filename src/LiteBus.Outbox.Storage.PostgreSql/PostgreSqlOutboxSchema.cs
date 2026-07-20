using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Storage.PostgreSql;
using Npgsql;

namespace LiteBus.Outbox.Storage.PostgreSql;

/// <summary>
///     Creates and validates the PostgreSQL outbox schema used by <see cref="PostgreSqlOutboxStore" />.
/// </summary>
/// <remarks>
///     <para>
///         LiteBus supports three schema ownership models:
///     </para>
///     <list type="number">
///         <item>
///             <description>
///                 <strong>Migration-owned (recommended for production).</strong> Copy the SQL files listed in
///                 <see cref="SqlFiles" /> or call <see cref="GetCreateScript(PostgreSqlOutboxStoreOptions?)" /> in your
///                 migration pipeline.
///             </description>
///         </item>
///         <item>
///             <description>
///                 <strong>Explicit bootstrap.</strong> Call
///                 <see cref="EnsureAsync(NpgsqlDataSource, PostgreSqlOutboxStoreOptions?, CancellationToken)" />
///                 during application startup or a deploy job.
///             </description>
///         </item>
///         <item>
///             <description>
///                 <strong>Opt-in host bootstrap.</strong> Set
///                 <see cref="PostgreSqlOutboxStoreOptions.EnsureSchemaCreationOnStartup" /> to <see langword="true" />
///                 and
///                 register the PostgreSQL outbox schema hosting module.
///             </description>
///         </item>
///     </list>
///     <para>
///         The outbox schema uses version 1. Its create script contains the complete current table shape, including
///         opaque payload text and lease fencing. LiteBus validates existing tables without mutating incompatible
///         shapes.
///     </para>
/// </remarks>
public static class PostgreSqlOutboxSchema
{
    /// <summary>
    ///     Gets the outbox table schema version implemented by this package release.
    /// </summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>
    ///     Gets the canonical SQL files shipped with the outbox PostgreSQL package.
    /// </summary>
    public static IReadOnlyList<PostgreSqlSchemaSqlFile> SqlFiles => PostgreSqlOutboxSchemaScripts.SqlFiles;

    /// <summary>
    ///     Returns the SQL script that creates the current outbox table, indexes, metadata table, and notify trigger.
    /// </summary>
    /// <param name="options">The schema and table options. Defaults create <c>public.litebus_outbox_messages</c>.</param>
    /// <returns>The canonical create script for <see cref="CurrentSchemaVersion" />.</returns>
    public static string GetCreateScript(PostgreSqlOutboxStoreOptions? options = null)
    {
        options ??= new PostgreSqlOutboxStoreOptions();
        return PostgreSqlOutboxSchemaScripts.BuildCreateScript(options);
    }

    /// <summary>
    ///     Creates the outbox schema when required.
    /// </summary>
    /// <param name="dataSource">The PostgreSQL data source.</param>
    /// <param name="options">The schema and table options.</param>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    /// <returns>A task that completes when the schema reaches the expected version.</returns>
    public static Task EnsureAsync(
        NpgsqlDataSource dataSource,
        PostgreSqlOutboxStoreOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dataSource);

        options ??= new PostgreSqlOutboxStoreOptions();

        return PostgreSqlSchemaManager.EnsureAsync(
            dataSource,
            options,
            PostgreSqlOutboxSchemaScripts.Definition,
            cancellationToken);
    }

    /// <summary>
    ///     Validates that the outbox table matches <see cref="CurrentSchemaVersion" />.
    /// </summary>
    /// <param name="dataSource">The PostgreSQL data source.</param>
    /// <param name="options">The schema and table options.</param>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    /// <returns>A task that completes when validation succeeds.</returns>
    /// <exception cref="PostgreSqlSchemaDriftException">
    ///     Thrown when the table is missing, incomplete, or recorded at an unexpected schema version.
    /// </exception>
    public static Task ValidateAsync(
        NpgsqlDataSource dataSource,
        PostgreSqlOutboxStoreOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dataSource);

        options ??= new PostgreSqlOutboxStoreOptions();

        return PostgreSqlSchemaManager.ValidateAsync(
            dataSource,
            options,
            PostgreSqlOutboxSchemaScripts.Definition,
            cancellationToken);
    }
}
