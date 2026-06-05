using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using LiteBus.Storage.PostgreSql;

namespace LiteBus.Outbox.Storage.PostgreSql;

/// <summary>
///     Builds outbox schema SQL scripts and supplies the component definition used by
///     <see cref="PostgreSqlSchemaManager" />.
/// </summary>
internal static class PostgreSqlOutboxSchemaScripts
{
    /// <summary>
    ///     The assembly that embeds outbox schema SQL resources.
    /// </summary>
    private static readonly Assembly Assembly = typeof(PostgreSqlOutboxSchemaScripts).Assembly;

    /// <summary>
    ///     The column names introduced by outbox schema version 1.
    /// </summary>
    internal static readonly IReadOnlyList<string> Version1Columns =
    [
        "message_id",
        "contract_name",
        "contract_version",
        "payload",
        "topic",
        "created_at",
        "visible_after",
        "status",
        "attempt_count",
        "lease_owner",
        "lease_expires_at",
        "last_error",
        "correlation_id",
        "causation_id",
        "tenant_id"
    ];

    /// <summary>
    ///     The column names introduced by outbox schema version 2.
    /// </summary>
    internal static readonly IReadOnlyList<string> Version2Columns =
    [
        "trace_context"
    ];

    /// <summary>
    ///     The column names introduced by outbox schema version 3.
    /// </summary>
    internal static readonly IReadOnlyList<string> Version3Columns =
    [
        "idempotency_key"
    ];

    /// <summary>
    ///     The schema objects introduced by outbox schema version 4 (insert notify trigger).
    /// </summary>
    internal static readonly IReadOnlyList<string> Version4Columns =
    [
    ];

    /// <summary>
    ///     The column names introduced by outbox schema version 5.
    /// </summary>
    internal static readonly IReadOnlyList<string> Version5Columns =
    [
        "published_at"
    ];

    /// <summary>
    ///     The ordered column groups introduced by each outbox schema version.
    /// </summary>
    internal static readonly IReadOnlyList<IReadOnlyList<string>> VersionColumnSets =
    [
        Version1Columns,
        Version2Columns,
        Version3Columns,
        Version4Columns,
        Version5Columns
    ];

    /// <summary>
    ///     Gets the canonical SQL files shipped with the outbox PostgreSQL package.
    /// </summary>
    internal static IReadOnlyList<PostgreSqlSchemaSqlFile> SqlFiles { get; } =
    [
        new PostgreSqlSchemaSqlFile(
            PostgreSqlOutboxSchemaSqlPaths.V1Create,
            "Creates the version 1 outbox table and indexes."),
        new PostgreSqlSchemaSqlFile(
            PostgreSqlOutboxSchemaSqlPaths.V1EnsureIndexes,
            "Ensures outbox indexes exist for the current schema version."),
        new PostgreSqlSchemaSqlFile(
            PostgreSqlOutboxSchemaSqlPaths.V2Upgrade,
            "Upgrades the outbox table from version 1 to version 2."),
        new PostgreSqlSchemaSqlFile(
            PostgreSqlOutboxSchemaSqlPaths.V3Upgrade,
            "Upgrades the outbox table from version 2 to version 3 (idempotency_key)."),
        new PostgreSqlSchemaSqlFile(
            PostgreSqlOutboxSchemaSqlPaths.V4Upgrade,
            "Upgrades the outbox table from version 3 to version 4 (insert notify trigger)."),
        new PostgreSqlSchemaSqlFile(
            PostgreSqlOutboxSchemaSqlPaths.V5Upgrade,
            "Upgrades the outbox table from version 4 to version 5 (published_at column).")
    ];

    /// <summary>
    ///     Gets the schema definition consumed by shared PostgreSQL schema bootstrap helpers.
    /// </summary>
    internal static PostgreSqlComponentSchemaDefinition Definition { get; } = new()
    {
        Component = PostgreSqlSchemaComponents.Outbox,
        CurrentSchemaVersion = PostgreSqlOutboxSchema.CurrentSchemaVersion,
        VersionColumnSets = VersionColumnSets,
        SqlFiles = SqlFiles,
        BuildVersion1CreateScript = BuildVersion1CreateScript,
        BuildUpgradeScript = BuildUpgradeScript,
        BuildEnsureIndexesScript = BuildEnsureIndexesScript,
        BuildCreateScript = BuildCreateScript,
        CreateLockKey = CreateLockKey,
        GetRequiredIndexNames = GetRequiredIndexNames
    };

    /// <summary>
    ///     Builds the full create script for one outbox schema version, including metadata DDL.
    /// </summary>
    /// <param name="options">The store table and metadata options.</param>
    /// <param name="version">The target schema version.</param>
    /// <returns>The rendered create SQL batch.</returns>
    internal static string BuildCreateScript(IPostgreSqlStoreTableOptions options, int version)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (version <= 0 || version > PostgreSqlOutboxSchema.CurrentSchemaVersion)
        {
            throw new ArgumentOutOfRangeException(nameof(version));
        }

        var builder = new StringBuilder();
        builder.AppendLine(PostgreSqlSchemaVersionStore.GetMetadataCreateScript(options));
        builder.AppendLine(BuildVersion1CreateScript(options));

        for (var currentVersion = 2; currentVersion <= version; currentVersion++)
        {
            builder.AppendLine(BuildUpgradeScript(options, currentVersion - 1, currentVersion));
        }

        return builder.ToString().TrimEnd();
    }

    /// <summary>
    ///     Builds the version 1 outbox create script with rendered identifier placeholders.
    /// </summary>
    /// <param name="options">The store table and metadata options.</param>
    /// <returns>The rendered version 1 create SQL batch.</returns>
    internal static string BuildVersion1CreateScript(IPostgreSqlStoreTableOptions options)
    {
        return PostgreSqlSqlScriptLoader.LoadAndRender(
            Assembly,
            PostgreSqlOutboxSchemaEmbeddedSql.V1Create,
            CreateStoreTokens(options));
    }

    /// <summary>
    ///     Builds the incremental upgrade script between two adjacent outbox schema versions.
    /// </summary>
    /// <param name="options">The store table and metadata options.</param>
    /// <param name="fromVersion">The source schema version.</param>
    /// <param name="toVersion">The target schema version.</param>
    /// <returns>The rendered upgrade SQL batch.</returns>
    internal static string BuildUpgradeScript(IPostgreSqlStoreTableOptions options, int fromVersion, int toVersion)
    {
        if (fromVersion + 1 != toVersion)
        {
            throw new ArgumentException("Upgrade scripts must advance exactly one schema version.", nameof(toVersion));
        }

        return toVersion switch
        {
            2 => PostgreSqlSchemaExecutor.LoadSharedAddTraceContextColumnScript(options),
            3 => PostgreSqlSchemaExecutor.LoadSharedAddIdempotencyKeyColumnScript(options),
            4 => PostgreSqlSqlScriptLoader.LoadAndRender(
                Assembly,
                PostgreSqlOutboxSchemaEmbeddedSql.V4Upgrade,
                CreateStoreTokens(options)),
            5 => PostgreSqlSqlScriptLoader.LoadAndRender(
                Assembly,
                PostgreSqlOutboxSchemaEmbeddedSql.V5Upgrade,
                CreateStoreTokens(options)),
            _ => throw new ArgumentOutOfRangeException(nameof(toVersion), toVersion, "Unsupported outbox schema version.")
        };
    }

    /// <summary>
    ///     Builds the script that ensures outbox indexes exist for the current schema version.
    /// </summary>
    /// <param name="options">The store table and metadata options.</param>
    /// <returns>The rendered index ensure SQL batch.</returns>
    internal static string BuildEnsureIndexesScript(IPostgreSqlStoreTableOptions options)
    {
        return PostgreSqlSqlScriptLoader.LoadAndRender(
            Assembly,
            PostgreSqlOutboxSchemaEmbeddedSql.V1EnsureIndexes,
            CreateStoreTokens(options));
    }

    /// <summary>
    ///     Returns the index names required for the current outbox schema version.
    /// </summary>
    /// <param name="options">The store table and metadata options.</param>
    /// <returns>The required index names for validation.</returns>
    internal static IReadOnlyList<string> GetRequiredIndexNames(IPostgreSqlStoreTableOptions options)
    {
        return
        [
            PostgreSqlIdentifier.UnquotedIndexName(options.TableName, "lease_idx"),
            PostgreSqlIdentifier.UnquotedIndexName(options.TableName, "topic_idx")
        ];
    }

    /// <summary>
    ///     Creates the advisory lock key used during outbox schema bootstrap.
    /// </summary>
    /// <param name="options">The store table options that identify the outbox table.</param>
    /// <returns>The stable advisory lock key.</returns>
    internal static string CreateLockKey(IPostgreSqlStoreTableOptions options)
    {
        return $"litebus:{PostgreSqlSchemaComponents.Outbox}:{options.SchemaName}:{options.TableName}";
    }

    /// <summary>
    ///     Builds the placeholder token map used by outbox SQL templates.
    /// </summary>
    /// <param name="options">The store table and metadata options.</param>
    /// <returns>The token map keyed by placeholder name without braces.</returns>
    private static Dictionary<string, string> CreateStoreTokens(IPostgreSqlStoreTableOptions options)
    {
        var tokens = PostgreSqlSchemaSqlTokens.ForStoreTable(options);
        tokens["LeaseIndexName"] = PostgreSqlIdentifier.IndexName(options.TableName, "lease_idx");
        tokens["TopicIndexName"] = PostgreSqlIdentifier.IndexName(options.TableName, "topic_idx");
        tokens["IdempotencyIndexName"] = PostgreSqlIdentifier.IndexName(options.TableName, "idempotency_idx");
        tokens["NotifyChannelName"] = PostgreSqlOutboxNotifyChannel.ChannelName;
        tokens["NotifyFunctionName"] = PostgreSqlIdentifier.UnquotedIndexName(options.TableName, "insert_notify_fn");
        tokens["NotifyTriggerName"] = PostgreSqlIdentifier.UnquotedIndexName(options.TableName, "insert_notify_trg");
        return tokens;
    }
}
