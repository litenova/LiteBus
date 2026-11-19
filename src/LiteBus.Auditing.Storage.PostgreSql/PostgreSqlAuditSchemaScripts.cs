using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using LiteBus.Storage.PostgreSql;

namespace LiteBus.Auditing.Storage.PostgreSql;

/// <summary>
///     Builds audit schema SQL scripts and supplies the table metadata
///     <see cref="PostgreSqlAuditSchema" /> validates against.
/// </summary>
internal static class PostgreSqlAuditSchemaScripts
{
    /// <summary>
    ///     The assembly that embeds audit schema SQL resources.
    /// </summary>
    private static readonly Assembly Assembly = typeof(PostgreSqlAuditSchemaScripts).Assembly;

    /// <summary>
    ///     The column names required by audit schema version 1 on the records table.
    /// </summary>
    internal static readonly IReadOnlyList<string> Version1RecordsColumns =
    [
        "document_key",
        "segment",
        "sequence",
        "previous_hash",
        "hash",
        "hash_scheme",
        "tenant_id",
        "document",
        "staged_at",
        "staged_order"
    ];

    /// <summary>
    ///     The column names required by audit schema version 1 on the heads table.
    /// </summary>
    internal static readonly IReadOnlyList<string> Version1HeadsColumns =
    [
        "segment",
        "tenant_id",
        "attributes",
        "opened_at",
        "last_sequence",
        "last_hash",
        "document_count",
        "last_checkpoint_sequence",
        "last_checkpoint_at",
        "expired",
        "version"
    ];

    /// <summary>
    ///     The column names required by audit schema version 1 on the checkpoints table.
    /// </summary>
    internal static readonly IReadOnlyList<string> Version1CheckpointsColumns =
    [
        "id",
        "segment",
        "sequence",
        "document_count",
        "hash",
        "hash_scheme",
        "kind",
        "created_at",
        "signature_scheme",
        "signature_value",
        "label",
        "publish_state",
        "publish_attempts",
        "last_publish_error",
        "signatures"
    ];

    /// <summary>
    ///     The database column types required by the current audit schema, keyed by column name.
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, string> RequiredColumnDataTypes =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["document"] = "jsonb",
            ["attributes"] = "jsonb",
            ["signatures"] = "jsonb"
        };

    /// <summary>
    ///     Gets the canonical SQL files shipped with the audit PostgreSQL package.
    /// </summary>
    internal static IReadOnlyList<PostgreSqlSchemaSqlFile> SqlFiles { get; } =
    [
        new(
            PostgreSqlAuditSchemaSqlPaths.V1Create,
            "Creates the version 1 audit records, heads, and checkpoints tables."),
        new(
            PostgreSqlAuditSchemaSqlPaths.V1EnsureIndexes,
            "Ensures the current audit indexes exist.")
    ];

    /// <summary>
    ///     Builds the full create script for the current audit schema, including metadata DDL.
    /// </summary>
    /// <param name="options">The store table and metadata options.</param>
    /// <returns>The rendered create SQL batch.</returns>
    internal static string BuildCreateScript(PostgreSqlAuditStoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var builder = new StringBuilder();
        builder.AppendLine(BuildMetadataCreateScript(options));
        builder.AppendLine(BuildBaselineCreateScript(options));
        builder.AppendLine(BuildEnsureIndexesScript(options));
        return builder.ToString().TrimEnd();
    }

    /// <summary>
    ///     Returns the rendered create script for the current audit schema.
    /// </summary>
    /// <param name="options">The audit store options.</param>
    /// <returns>The create script.</returns>
    internal static string GetCreateScript(PostgreSqlAuditStoreOptions? options = null)
    {
        options ??= new PostgreSqlAuditStoreOptions();
        return BuildCreateScript(options);
    }

    /// <summary>
    ///     Builds the baseline audit create script with rendered identifier placeholders.
    /// </summary>
    /// <param name="options">The store table and metadata options.</param>
    /// <returns>The rendered current-version create SQL batch.</returns>
    internal static string BuildBaselineCreateScript(PostgreSqlAuditStoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return PostgreSqlAuditSchemaSqlLoader.LoadAndRender(
            Assembly,
            PostgreSqlAuditSchemaEmbeddedSql.V1Create,
            CreateStoreTokens(options));
    }

    /// <summary>
    ///     Builds the script that ensures the current audit indexes exist.
    /// </summary>
    /// <param name="options">The store table and metadata options.</param>
    /// <returns>The rendered index ensure SQL batch.</returns>
    internal static string BuildEnsureIndexesScript(PostgreSqlAuditStoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return PostgreSqlAuditSchemaSqlLoader.LoadAndRender(
            Assembly,
            PostgreSqlAuditSchemaEmbeddedSql.V1EnsureIndexes,
            CreateStoreTokens(options));
    }

    /// <summary>
    ///     Builds the script that creates the shared schema version metadata table.
    /// </summary>
    /// <param name="options">The store table and metadata options.</param>
    /// <returns>The rendered metadata create SQL batch.</returns>
    /// <remarks>
    ///     The metadata table is shared by every LiteBus PostgreSQL store: one row per component and physical
    ///     table records which schema version was applied. This statement replicates the shared metadata shape
    ///     exactly, so the audit version rows sit beside the inbox, outbox, saga, and recurring rows.
    /// </remarks>
    internal static string BuildMetadataCreateScript(PostgreSqlAuditStoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var metadataTable = PostgreSqlIdentifier.Qualify(options.MetadataSchemaName, options.MetadataTableName);

        return $"""
                CREATE SCHEMA IF NOT EXISTS {PostgreSqlIdentifier.Quote(options.MetadataSchemaName)};

                CREATE TABLE IF NOT EXISTS {metadataTable}
                (
                    component text NOT NULL,
                    schema_name text NOT NULL,
                    table_name text NOT NULL,
                    version integer NOT NULL,
                    applied_at timestamptz NOT NULL,
                    PRIMARY KEY (component, schema_name, table_name)
                );
                """;
    }

    /// <summary>
    ///     Returns the index names required for the current audit schema.
    /// </summary>
    /// <param name="options">The store table and metadata options.</param>
    /// <returns>The required index names for validation.</returns>
    internal static IReadOnlyList<string> GetRequiredIndexNames(PostgreSqlAuditStoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return
        [
            PostgreSqlIdentifier.UnquotedIndexName(options.TableName, "sequence_idx"),
            PostgreSqlIdentifier.UnquotedIndexName(options.TableName, "unlinked_idx"),
            PostgreSqlIdentifier.UnquotedIndexName(options.TableName, "tenant_idx"),
            PostgreSqlIdentifier.UnquotedIndexName(options.HeadsTableName, "tenant_idx"),
            PostgreSqlIdentifier.UnquotedIndexName(options.HeadsTableName, "opened_idx"),
            PostgreSqlIdentifier.UnquotedIndexName(options.CheckpointsTableName, "segment_idx"),
            PostgreSqlIdentifier.UnquotedIndexName(options.CheckpointsTableName, "unpublished_idx")
        ];
    }

    /// <summary>
    ///     Creates the advisory lock key used during audit schema bootstrap.
    /// </summary>
    /// <param name="options">The store table options that identify the audit records table.</param>
    /// <returns>The stable advisory lock key.</returns>
    internal static string CreateLockKey(PostgreSqlAuditStoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return $"litebus:{PostgreSqlAuditSchema.ComponentName}:{options.SchemaName}:{options.TableName}";
    }

    /// <summary>
    ///     Builds the placeholder token map used by audit SQL templates.
    /// </summary>
    /// <param name="options">The store table and metadata options.</param>
    /// <returns>The token map keyed by placeholder name without braces.</returns>
    internal static Dictionary<string, string> CreateStoreTokens(PostgreSqlAuditStoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["QuotedSchemaName"] = PostgreSqlIdentifier.Quote(options.SchemaName),
            ["QualifiedRecordsTableName"] = PostgreSqlIdentifier.Qualify(options.SchemaName, options.TableName),
            ["QualifiedHeadsTableName"] = PostgreSqlIdentifier.Qualify(options.SchemaName, options.HeadsTableName),
            ["QualifiedCheckpointsTableName"] = PostgreSqlIdentifier.Qualify(
                options.SchemaName,
                options.CheckpointsTableName),
            ["RecordsSequenceIndexName"] = PostgreSqlIdentifier.IndexName(options.TableName, "sequence_idx"),
            ["RecordsUnlinkedIndexName"] = PostgreSqlIdentifier.IndexName(options.TableName, "unlinked_idx"),
            ["RecordsTenantIndexName"] = PostgreSqlIdentifier.IndexName(options.TableName, "tenant_idx"),
            ["HeadsTenantIndexName"] = PostgreSqlIdentifier.IndexName(options.HeadsTableName, "tenant_idx"),
            ["HeadsOpenedIndexName"] = PostgreSqlIdentifier.IndexName(options.HeadsTableName, "opened_idx"),
            ["CheckpointsSegmentIndexName"] = PostgreSqlIdentifier.IndexName(
                options.CheckpointsTableName,
                "segment_idx"),
            ["CheckpointsUnpublishedIndexName"] = PostgreSqlIdentifier.IndexName(
                options.CheckpointsTableName,
                "unpublished_idx")
        };
    }
}
