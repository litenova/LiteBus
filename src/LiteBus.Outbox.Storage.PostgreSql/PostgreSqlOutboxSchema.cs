using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Outbox.Abstractions;
using LiteBus.Storage.PostgreSql;
using LiteBus.Storage.PostgreSql.Stores;
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
    ///     Builds the parameterized insert for one outbox envelope against the current schema version.
    /// </summary>
    /// <param name="envelope">The envelope to insert.</param>
    /// <param name="options">The schema and table options. Defaults create <c>public.litebus_outbox_messages</c>.</param>
    /// <returns>The insert statement with values in placeholder order.</returns>
    /// <remarks>
    ///     <para>
    ///         The statement targets <see cref="CurrentSchemaVersion" /> with the same columns and value conversions
    ///         <see cref="PostgreSqlOutboxStore" /> writes, so a unit of work that queues SQL writes the exact row the
    ///         PostgreSQL store writes. Callers must not edit the returned statement: a schema change updates this
    ///         method in one place.
    ///     </para>
    ///     <para>
    ///         The statement ends with <c>ON CONFLICT DO NOTHING</c> and has no <c>RETURNING</c> clause, so a
    ///         conflicting insert is skipped silently. Idempotency resolution (Strict and ReturnExisting) is the
    ///         caller job: resolve duplicates before queueing rather than reading them back from a skipped insert.
    ///     </para>
    /// </remarks>
    public static PostgreSqlOutboxInsertStatement CreateInsertStatement(
        OutboxEnvelope envelope,
        PostgreSqlOutboxStoreOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        options ??= new PostgreSqlOutboxStoreOptions();
        var tableName = PostgreSqlIdentifier.Qualify(options.SchemaName, options.TableName);

        var sql = $"""
                   INSERT INTO {tableName} (
                       message_id,
                       contract_name,
                       contract_version,
                       payload,
                       topic,
                       created_at,
                       visible_after,
                       status,
                       attempt_count,
                       lease_owner,
                       lease_expires_at,
                       last_error,
                       correlation_id,
                       causation_id,
                       tenant_id,
                       idempotency_key,
                       trace_context,
                       lease_generation)
                   VALUES (
                       $1,
                       $2,
                       $3,
                       $4,
                       $5,
                       $6,
                       $7,
                       $8,
                       $9,
                       $10,
                       $11,
                       $12,
                       $13,
                       $14,
                       $15,
                       $16,
                       $17::jsonb,
                       $18)
                   ON CONFLICT DO NOTHING
                   """;

        object[] values =
        [
            envelope.Id,
            envelope.ContractName,
            envelope.ContractVersion,
            envelope.Payload,
            (object?) envelope.Topic ?? DBNull.Value,
            envelope.CreatedAt,
            (object?) envelope.VisibleAfter ?? DBNull.Value,
            (int) envelope.Status,
            envelope.AttemptCount,
            (object?) envelope.LeaseOwner ?? DBNull.Value,
            (object?) envelope.LeaseExpiresAt ?? DBNull.Value,
            (object?) envelope.LastError ?? DBNull.Value,
            (object?) envelope.CorrelationId ?? DBNull.Value,
            (object?) envelope.CausationId ?? DBNull.Value,
            PostgreSqlIdempotencyResolution.NormalizeTenantParameter(envelope.TenantId),
            (object?) envelope.IdempotencyKey ?? DBNull.Value,
            string.IsNullOrWhiteSpace(envelope.TraceContext) ? DBNull.Value : envelope.TraceContext,
            envelope.LeaseGeneration
        ];

        return new PostgreSqlOutboxInsertStatement { Sql = sql, Values = values };
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
