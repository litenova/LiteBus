using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Storage.PostgreSql;
using Npgsql;

namespace LiteBus.Commands.Storage.PostgreSql;

/// <summary>
///     Creates and validates the PostgreSQL idempotency schema used by <see cref="PostgreSqlIdempotencyStore" />.
/// </summary>
/// <remarks>
///     <para>
///         LiteBus supports three schema ownership models:
///     </para>
///     <list type="number">
///         <item>
///             <description>
///                 <strong>Migration-owned (recommended for production).</strong> Copy the SQL files listed in
///                 <see cref="SqlFiles" /> or call <see cref="GetCreateScript(PostgreSqlIdempotencyStoreOptions?)" /> in your
///                 migration pipeline.
///             </description>
///         </item>
///         <item>
///             <description>
///                 <strong>Explicit bootstrap.</strong> Call
///                 <see cref="EnsureAsync(NpgsqlDataSource, PostgreSqlIdempotencyStoreOptions?, CancellationToken)" />
///                 during application startup or a deploy job.
///             </description>
///         </item>
///         <item>
///             <description>
///                 <strong>Opt-in host bootstrap.</strong> Set
///                 <see cref="PostgreSqlIdempotencyStoreOptions.EnsureSchemaCreationOnStartup" /> to <see langword="true" /> and
///                 register the PostgreSQL idempotency storage module.
///             </description>
///         </item>
///     </list>
///     <para>
///         The idempotency schema uses version 1. Its table holds one row per key with the recorded payload, the
///         fingerprint the key was claimed with, the token fencing the current claim, and the flag that separates a
///         claim still in flight from a key already applied.
///     </para>
/// </remarks>
public static class PostgreSqlIdempotencySchema
{
    /// <summary>
    ///     Gets the idempotency table schema version implemented by this package release.
    /// </summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>
    ///     Gets the component name recorded in the schema version metadata table.
    /// </summary>
    public const string ComponentName = "idempotency";

    /// <summary>
    ///     Gets the canonical SQL files shipped with the idempotency PostgreSQL package.
    /// </summary>
    /// <remarks>
    ///     Paths are relative to the repository root, for example
    ///     <c>src/LiteBus.Commands.Storage.PostgreSql/Sql/idempotency/v1/create.sql</c>. Replace
    ///     <c>{{TokenName}}</c> placeholders with quoted identifiers for your schema and table names before running the
    ///     scripts manually, or call <see cref="GetCreateScript(PostgreSqlIdempotencyStoreOptions?)" /> to render them.
    /// </remarks>
    public static IReadOnlyList<PostgreSqlSchemaSqlFile> SqlFiles => PostgreSqlIdempotencySchemaScripts.SqlFiles;

    /// <summary>
    ///     Returns the SQL script that creates the current idempotency table, indexes, and metadata table.
    /// </summary>
    /// <param name="options">The schema and table options. Defaults create <c>public.litebus_idempotency_keys</c>.</param>
    /// <returns>The canonical create script for <see cref="CurrentSchemaVersion" />.</returns>
    public static string GetCreateScript(PostgreSqlIdempotencyStoreOptions? options = null)
    {
        return PostgreSqlIdempotencySchemaScripts.GetCreateScript(options);
    }

    /// <summary>
    ///     Creates the idempotency schema when required.
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
        PostgreSqlIdempotencyStoreOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dataSource);

        options ??= new PostgreSqlIdempotencyStoreOptions();

        return PostgreSqlSchemaManager.EnsureAsync(
            dataSource,
            options,
            PostgreSqlIdempotencySchemaScripts.Definition,
            cancellationToken);
    }

    /// <summary>
    ///     Validates that the idempotency table matches <see cref="CurrentSchemaVersion" />.
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
        PostgreSqlIdempotencyStoreOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dataSource);

        options ??= new PostgreSqlIdempotencyStoreOptions();

        return PostgreSqlSchemaManager.ValidateAsync(
            dataSource,
            options,
            PostgreSqlIdempotencySchemaScripts.Definition,
            cancellationToken);
    }
}
