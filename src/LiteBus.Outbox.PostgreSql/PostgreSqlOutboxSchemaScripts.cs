using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using LiteBus.PostgreSql;

namespace LiteBus.Outbox.PostgreSql;

internal static class PostgreSqlOutboxSchemaScripts
{
    private static readonly Assembly Assembly = typeof(PostgreSqlOutboxSchemaScripts).Assembly;

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

    internal static readonly IReadOnlyList<string> Version2Columns =
    [
        "trace_context"
    ];

    internal static readonly IReadOnlyList<IReadOnlyList<string>> VersionColumnSets =
    [
        Version1Columns,
        Version2Columns
    ];

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
            "Upgrades the outbox table from version 1 to version 2.")
    ];

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
        CreateLockKey = CreateLockKey
    };

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

    internal static string BuildVersion1CreateScript(IPostgreSqlStoreTableOptions options)
    {
        return PostgreSqlSqlScriptLoader.LoadAndRender(
            Assembly,
            PostgreSqlOutboxSchemaEmbeddedSql.V1Create,
            CreateStoreTokens(options));
    }

    internal static string BuildUpgradeScript(IPostgreSqlStoreTableOptions options, int fromVersion, int toVersion)
    {
        if (fromVersion + 1 != toVersion)
        {
            throw new ArgumentException("Upgrade scripts must advance exactly one schema version.", nameof(toVersion));
        }

        return toVersion switch
        {
            2 => PostgreSqlSchemaExecutor.LoadSharedAddTraceContextColumnScript(options),
            _ => throw new ArgumentOutOfRangeException(nameof(toVersion), toVersion, "Unsupported outbox schema version.")
        };
    }

    internal static string BuildEnsureIndexesScript(IPostgreSqlStoreTableOptions options)
    {
        return PostgreSqlSqlScriptLoader.LoadAndRender(
            Assembly,
            PostgreSqlOutboxSchemaEmbeddedSql.V1EnsureIndexes,
            CreateStoreTokens(options));
    }

    internal static string CreateLockKey(IPostgreSqlStoreTableOptions options)
    {
        return $"litebus:{PostgreSqlSchemaComponents.Outbox}:{options.SchemaName}:{options.TableName}";
    }

    private static Dictionary<string, string> CreateStoreTokens(IPostgreSqlStoreTableOptions options)
    {
        var tokens = PostgreSqlSchemaSqlTokens.ForStoreTable(options);
        tokens["LeaseIndexName"] = PostgreSqlIdentifier.IndexName(options.TableName, "lease_idx");
        tokens["TopicIndexName"] = PostgreSqlIdentifier.IndexName(options.TableName, "topic_idx");
        return tokens;
    }
}
