using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using LiteBus.Storage.PostgreSql;

namespace LiteBus.Inbox.Storage.PostgreSql;

/// <summary>
///     Builds inbox schema SQL scripts and supplies the component definition used by
///     <see cref="PostgreSqlSchemaManager" />.
/// </summary>
internal static class PostgreSqlInboxSchemaScripts
{
    /// <summary>
    ///     The assembly that embeds inbox schema SQL resources.
    /// </summary>
    private static readonly Assembly Assembly = typeof(PostgreSqlInboxSchemaScripts).Assembly;

    /// <summary>
    ///     The column names required by inbox schema version 1.
    /// </summary>
    internal static readonly IReadOnlyList<string> Version1Columns =
    [
        "message_id",
        "contract_name",
        "contract_version",
        "payload",
        "created_at",
        "visible_after",
        "attempt_count",
        "status",
        "idempotency_key",
        "lease_owner",
        "lease_expires_at",
        "last_error",
        "correlation_id",
        "causation_id",
        "tenant_id",
        "trace_context",
        "completed_at",
        "last_attempted_at",
        "first_failed_at",
        "dead_lettered_at",
        "last_lease_owner",
        "error_type",
        "lease_generation"
    ];

    /// <summary>
    ///     The ordered column groups introduced by each inbox schema version.
    /// </summary>
    internal static readonly IReadOnlyList<IReadOnlyList<string>> VersionColumnSets =
    [
        Version1Columns
    ];

    /// <summary>
    ///     The database column types required by the current inbox schema.
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, string> RequiredColumnDataTypes =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["payload"] = "text",
            ["lease_generation"] = "bigint"
        };

    /// <summary>
    ///     Gets the canonical SQL files shipped with the inbox PostgreSQL package.
    /// </summary>
    internal static IReadOnlyList<PostgreSqlSchemaSqlFile> SqlFiles { get; } =
    [
        new(
            PostgreSqlInboxSchemaSqlPaths.V1Create,
            "Creates the current inbox table, indexes, and optional insert notify trigger for a new installation."),
        new(
            PostgreSqlInboxSchemaSqlPaths.V1EnsureIndexes,
            "Ensures the current inbox indexes exist.")
    ];

    /// <summary>
    ///     Gets the schema definition consumed by shared PostgreSQL schema bootstrap helpers.
    /// </summary>
    internal static PostgreSqlComponentSchemaDefinition Definition { get; } = new()
    {
        Component = PostgreSqlSchemaComponents.Inbox,
        CurrentSchemaVersion = PostgreSqlInboxSchema.CurrentSchemaVersion,
        VersionColumnSets = VersionColumnSets,
        RequiredColumnDataTypes = RequiredColumnDataTypes,
        SqlFiles = SqlFiles,
        BuildBaselineCreateScript = BuildBaselineCreateScript,
        BuildEnsureIndexesScript = BuildEnsureIndexesScript,
        BuildCreateScript = BuildCreateScript,
        CreateLockKey = CreateLockKey,
        GetRequiredIndexNames = GetRequiredIndexNames
    };

    /// <summary>
    ///     Builds the full create script for the current inbox schema, including metadata DDL.
    /// </summary>
    /// <param name="options">The store table and metadata options.</param>
    /// <returns>The rendered create SQL batch.</returns>
    internal static string BuildCreateScript(IPostgreSqlStoreTableOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var builder = new StringBuilder();
        builder.AppendLine(PostgreSqlSchemaVersionStore.GetMetadataCreateScript(options));
        builder.AppendLine(BuildBaselineCreateScript(options));
        builder.AppendLine(BuildEnsureIndexesScript(options));
        return builder.ToString().TrimEnd();
    }

    /// <summary>
    ///     Builds the baseline inbox create script with rendered identifier placeholders.
    /// </summary>
    /// <param name="options">The store table and metadata options.</param>
    /// <returns>The rendered current-version create SQL batch.</returns>
    internal static string BuildBaselineCreateScript(IPostgreSqlStoreTableOptions options)
    {
        return PostgreSqlSqlScriptLoader.LoadAndRender(
            Assembly,
            PostgreSqlInboxSchemaEmbeddedSql.V1Create,
            CreateStoreTokens(options));
    }

    /// <summary>
    ///     Builds the script that ensures the current inbox indexes exist.
    /// </summary>
    /// <param name="options">The store table and metadata options.</param>
    /// <returns>The rendered index ensure SQL batch.</returns>
    internal static string BuildEnsureIndexesScript(IPostgreSqlStoreTableOptions options)
    {
        return PostgreSqlSqlScriptLoader.LoadAndRender(
            Assembly,
            PostgreSqlInboxSchemaEmbeddedSql.V1EnsureIndexes,
            CreateStoreTokens(options));
    }

    /// <summary>
    ///     Returns the index names required for the current inbox schema.
    /// </summary>
    /// <param name="options">The store table and metadata options.</param>
    /// <returns>The required index names for validation.</returns>
    internal static IReadOnlyList<string> GetRequiredIndexNames(IPostgreSqlStoreTableOptions options)
    {
        return
        [
            PostgreSqlIdentifier.UnquotedIndexName(options.TableName, "idempotency_key_uidx"),
            PostgreSqlIdentifier.UnquotedIndexName(options.TableName, "lease_idx")
        ];
    }

    /// <summary>
    ///     Creates the advisory lock key used during inbox schema bootstrap.
    /// </summary>
    /// <param name="options">The store table options that identify the inbox table.</param>
    /// <returns>The stable advisory lock key.</returns>
    internal static string CreateLockKey(IPostgreSqlStoreTableOptions options)
    {
        return $"litebus:{PostgreSqlSchemaComponents.Inbox}:{options.SchemaName}:{options.TableName}";
    }

    /// <summary>
    ///     Builds the placeholder token map used by inbox SQL templates.
    /// </summary>
    /// <param name="options">The store table and metadata options.</param>
    /// <returns>The token map keyed by placeholder name without braces.</returns>
    private static Dictionary<string, string> CreateStoreTokens(IPostgreSqlStoreTableOptions options)
    {
        var tokens = PostgreSqlSchemaSqlTokens.ForStoreTable(options);
        tokens["IdempotencyIndexName"] = PostgreSqlIdentifier.IndexName(options.TableName, "idempotency_key_uidx");
        tokens["LeaseIndexName"] = PostgreSqlIdentifier.IndexName(options.TableName, "lease_idx");
        tokens["UnquotedSchemaName"] = options.SchemaName;
        tokens["UnquotedTableName"] = options.TableName;
        tokens["QuotedTableName"] = PostgreSqlIdentifier.Quote(options.TableName);
        tokens["NotifyChannelName"] = PostgreSqlInboxNotifyChannel.ChannelName;
        tokens["NotifyFunctionName"] = PostgreSqlIdentifier.UnquotedIndexName(options.TableName, "insert_notify_fn");
        tokens["NotifyTriggerName"] = PostgreSqlIdentifier.UnquotedIndexName(options.TableName, "insert_notify_trg");
        return tokens;
    }
}
