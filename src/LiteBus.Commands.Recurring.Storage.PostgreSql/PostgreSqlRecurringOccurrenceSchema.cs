using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Storage.PostgreSql;
using Npgsql;

namespace LiteBus.Commands.Recurring.Storage.PostgreSql;

/// <summary>
///     Creates and validates the PostgreSQL recurring occurrence schema used by <see cref="PostgreSqlRecurringOccurrenceStore" />.
/// </summary>
/// <remarks>
///     <para>
///         LiteBus supports three schema ownership models:
///     </para>
///     <list type="number">
///         <item>
///             <description>
///                 <strong>Migration-owned (recommended for production).</strong> Copy the SQL files listed in
///                 <see cref="SqlFiles" /> or call <see cref="GetCreateScript(PostgreSqlRecurringLeaseOptions?)" /> in your
///                 migration pipeline.
///             </description>
///         </item>
///         <item>
///             <description>
///                 <strong>Explicit bootstrap.</strong> Call
///                 <see cref="EnsureAsync(NpgsqlDataSource, PostgreSqlRecurringLeaseOptions?, CancellationToken)" />
///                 during application startup or a deploy job.
///             </description>
///         </item>
///         <item>
///             <description>
///                 <strong>Opt-in host bootstrap.</strong> Set
///                 <see cref="PostgreSqlRecurringLeaseOptions.EnsureSchemaCreationOnStartup" /> to <see langword="true" /> and
///                 register the PostgreSQL recurring lease module.
///             </description>
///         </item>
///     </list>
///     <para>
///         The recurring occurrence schema uses version 2. Version 1 shipped only the leases table; version 2 adds the
///         occurrences table beside it. Each row records one scheduled occurrence a loop ran, keyed by the stable
///         occurrence key, so a restarting loop can decide whether one catch-up pass is still due. Version metadata rows
///         are keyed per table, so the leases table stays at version 1 while the occurrences table records version 2.
///     </para>
/// </remarks>
public static class PostgreSqlRecurringOccurrenceSchema
{
    /// <summary>
    ///     Gets the recurring occurrences table schema version implemented by this package release.
    /// </summary>
    public const int CurrentSchemaVersion = 2;

    /// <summary>
    ///     Gets the component name recorded in the schema version metadata table.
    /// </summary>
    public const string ComponentName = "recurring";

    /// <summary>
    ///     Gets the canonical occurrence SQL files shipped with the recurring PostgreSQL package.
    /// </summary>
    /// <remarks>
    ///     Paths are relative to the repository root, for example
    ///     <c>src/LiteBus.Commands.Recurring.Storage.PostgreSql/Sql/recurring/v2/create.sql</c>. Replace
    ///     <c>{{TokenName}}</c> placeholders with quoted identifiers for your schema and table names before running the
    ///     scripts manually, or call <see cref="GetCreateScript(PostgreSqlRecurringLeaseOptions?)" /> to render them.
    /// </remarks>
    public static IReadOnlyList<PostgreSqlSchemaSqlFile> SqlFiles => PostgreSqlRecurringOccurrenceSchemaScripts.SqlFiles;

    /// <summary>
    ///     Returns the SQL script that creates the current recurring occurrences table, indexes, and metadata table.
    /// </summary>
    /// <param name="options">The schema and table options. Defaults create <c>public.litebus_recurring_occurrences</c>.</param>
    /// <returns>The canonical create script for <see cref="CurrentSchemaVersion" />.</returns>
    public static string GetCreateScript(PostgreSqlRecurringLeaseOptions? options = null)
    {
        options ??= new PostgreSqlRecurringLeaseOptions();

        return PostgreSqlRecurringOccurrenceSchemaScripts.BuildCreateScript(new OccurrenceTableOptions(options));
    }

    /// <summary>
    ///     Creates the recurring occurrence schema when required.
    /// </summary>
    /// <param name="dataSource">The PostgreSQL data source.</param>
    /// <param name="options">The schema and table options.</param>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    /// <returns>A task that completes when the schema reaches the expected version.</returns>
    /// <remarks>
    ///     The operation is idempotent and safe to run from multiple application instances. One instance acquires a
    ///     PostgreSQL advisory lock while creating the schema; the others wait until the schema reaches the expected version.
    /// </remarks>
    public static Task EnsureAsync(
        NpgsqlDataSource dataSource,
        PostgreSqlRecurringLeaseOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dataSource);

        options ??= new PostgreSqlRecurringLeaseOptions();

        return PostgreSqlSchemaManager.EnsureAsync(
            dataSource,
            new OccurrenceTableOptions(options),
            PostgreSqlRecurringOccurrenceSchemaScripts.Definition,
            cancellationToken);
    }

    /// <summary>
    ///     Validates that the recurring occurrences table matches <see cref="CurrentSchemaVersion" />.
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
        PostgreSqlRecurringLeaseOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dataSource);

        options ??= new PostgreSqlRecurringLeaseOptions();

        return PostgreSqlSchemaManager.ValidateAsync(
            dataSource,
            new OccurrenceTableOptions(options),
            PostgreSqlRecurringOccurrenceSchemaScripts.Definition,
            cancellationToken);
    }

    /// <summary>
    ///     Presents the occurrences table of <see cref="PostgreSqlRecurringLeaseOptions" /> as the store table the shared
    ///     schema helpers bootstrap.
    /// </summary>
    /// <remarks>
    ///     The shared schema manager bootstraps one table per definition and reads that table from
    ///     <see cref="IPostgreSqlStoreTableOptions.TableName" />, which names the leases table on the module options.
    ///     This adapter repoints the store table at <see cref="PostgreSqlRecurringLeaseOptions.OccurrenceTableName" />
    ///     while keeping the schema, metadata, logging, and index-validation settings, so occurrence bootstrap reuses the
    ///     same advisory locking, idempotent scripts, and drift errors as the leases table.
    /// </remarks>
    private sealed record OccurrenceTableOptions : PostgreSqlSchemaStoreOptions, IPostgreSqlStoreTableOptions
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="OccurrenceTableOptions" /> class.
        /// </summary>
        /// <param name="options">The module options supplying the schema and occurrence table names.</param>
        public OccurrenceTableOptions(PostgreSqlRecurringLeaseOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);

            SchemaName = options.SchemaName;
            TableName = options.OccurrenceTableName;
            MetadataSchemaName = options.MetadataSchemaName;
            MetadataTableName = options.MetadataTableName;
            Logger = options.Logger;
            ValidateIndexesOnStartup = options.ValidateIndexesOnStartup;
        }

        /// <summary>
        ///     Gets the PostgreSQL schema that contains the recurring occurrences table.
        /// </summary>
        public string SchemaName { get; }

        /// <summary>
        ///     Gets the recurring occurrences table name.
        /// </summary>
        public string TableName { get; }
    }
}
