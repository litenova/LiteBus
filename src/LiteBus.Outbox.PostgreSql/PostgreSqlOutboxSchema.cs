using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.PostgreSql;
using Npgsql;

namespace LiteBus.Outbox.PostgreSql;

/// <summary>
///     Creates, upgrades, and validates the PostgreSQL outbox schema used by <see cref="PostgreSqlOutboxStore" />.
/// </summary>
/// <remarks>
///     <para>
///         LiteBus supports three schema ownership models:
///     </para>
///     <list type="number">
///         <item>
///             <description>
///                 <strong>Migration-owned (recommended for production).</strong> Copy the SQL files listed in
///                 <see cref="SqlFiles" /> or call <see cref="GetCreateScript(PostgreSqlOutboxStoreOptions?)" /> /
///                 <see cref="GetUpgradeScript(int, int, PostgreSqlOutboxStoreOptions?)" /> in your migration pipeline.
///             </description>
///         </item>
///         <item>
///             <description>
///                 <strong>Explicit bootstrap.</strong> Call <see cref="EnsureAsync(NpgsqlDataSource, PostgreSqlOutboxStoreOptions?, CancellationToken)" />
///                 during application startup or a deploy job.
///             </description>
///         </item>
///         <item>
///             <description>
///                 <strong>Opt-in host bootstrap.</strong> Set
///                 <see cref="PostgreSqlOutboxStoreOptions.EnsureSchemaCreationOnStartup" /> to <see langword="true" /> and
///                 register the PostgreSQL outbox schema hosting module.
///             </description>
///         </item>
///     </list>
///     <para>
///         Physical table schema version is tracked separately from message contract version stored on each row. Contract
///         version describes payload shape; table schema version describes columns and indexes managed by LiteBus.
///     </para>
/// </remarks>
public static class PostgreSqlOutboxSchema
{
    /// <summary>
    ///     Gets the outbox table schema version implemented by this package release.
    /// </summary>
    public const int CurrentSchemaVersion = 2;

    /// <summary>
    ///     Gets the canonical SQL files shipped with the outbox PostgreSQL package.
    /// </summary>
    public static IReadOnlyList<PostgreSqlSchemaSqlFile> SqlFiles => PostgreSqlOutboxSchemaScripts.SqlFiles;

    /// <summary>
    ///     Returns the SQL script that creates the current outbox schema, indexes, and metadata table.
    /// </summary>
    /// <param name="options">The schema and table options. Defaults create <c>public.litebus_outbox_messages</c>.</param>
    /// <returns>The canonical create script for <see cref="CurrentSchemaVersion" />.</returns>
    public static string GetCreateScript(PostgreSqlOutboxStoreOptions? options = null)
    {
        options ??= new PostgreSqlOutboxStoreOptions();
        return PostgreSqlOutboxSchemaScripts.BuildCreateScript(options, CurrentSchemaVersion);
    }

    /// <summary>
    ///     Returns the SQL script that upgrades the outbox schema from one version to the next.
    /// </summary>
    /// <param name="fromVersion">The source schema version.</param>
    /// <param name="toVersion">The target schema version.</param>
    /// <param name="options">The schema and table options.</param>
    /// <returns>The upgrade script.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     Thrown when the requested version range is unsupported.
    /// </exception>
    public static string GetUpgradeScript(int fromVersion, int toVersion, PostgreSqlOutboxStoreOptions? options = null)
    {
        options ??= new PostgreSqlOutboxStoreOptions();

        if (fromVersion <= 0 || toVersion <= 0 || fromVersion >= toVersion || toVersion > CurrentSchemaVersion)
        {
            throw new ArgumentOutOfRangeException(
                nameof(toVersion),
                toVersion,
                $"Outbox schema upgrades must advance from a positive version to at most {CurrentSchemaVersion}.");
        }

        var builder = new System.Text.StringBuilder();

        for (var version = fromVersion + 1; version <= toVersion; version++)
        {
            builder.AppendLine(PostgreSqlOutboxSchemaScripts.BuildUpgradeScript(options, version - 1, version));
        }

        return builder.ToString().TrimEnd();
    }

    /// <summary>
    ///     Creates or upgrades the outbox schema to <see cref="CurrentSchemaVersion" /> when required.
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
    ///     Creates the outbox table and indexes when they do not exist.
    /// </summary>
    /// <param name="dataSource">The PostgreSQL data source.</param>
    /// <param name="options">The schema and table options.</param>
    /// <param name="cancellationToken">A token used to cancel the database command.</param>
    /// <returns>A task that completes when the schema reaches the expected version.</returns>
    public static Task CreateIfNotExistsAsync(
        NpgsqlDataSource dataSource,
        PostgreSqlOutboxStoreOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        return EnsureAsync(dataSource, options, cancellationToken);
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
