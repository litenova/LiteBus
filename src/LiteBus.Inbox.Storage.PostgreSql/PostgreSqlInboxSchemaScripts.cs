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
    ///     The column names introduced by inbox schema version 1.
    /// </summary>
    internal static readonly IReadOnlyList<string> Version1Columns =
    [
        "command_id",
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
        "tenant_id"
    ];

    /// <summary>
    ///     The column names introduced by inbox schema version 2.
    /// </summary>
    internal static readonly IReadOnlyList<string> Version2Columns =
    [
        "trace_context"
    ];

    /// <summary>
    ///     The ordered column groups introduced by each inbox schema version.
    /// </summary>
    internal static readonly IReadOnlyList<IReadOnlyList<string>> VersionColumnSets =
    [
        Version1Columns,
        Version2Columns
    ];

    /// <summary>
    ///     Gets the canonical SQL files shipped with the inbox PostgreSQL package.
    /// </summary>
    internal static IReadOnlyList<PostgreSqlSchemaSqlFile> SqlFiles { get; } =
    [
        new PostgreSqlSchemaSqlFile(
            PostgreSqlInboxSchemaSqlPaths.V1Create,
            "Creates the version 1 command inbox table and indexes."),
        new PostgreSqlSchemaSqlFile(
            PostgreSqlInboxSchemaSqlPaths.V1EnsureIndexes,
            "Ensures command inbox indexes exist for the current schema version."),
        new PostgreSqlSchemaSqlFile(
            PostgreSqlInboxSchemaSqlPaths.V2Upgrade,
            "Upgrades the command inbox table from version 1 to version 2.")
    ];

    /// <summary>
    ///     Gets the schema definition consumed by shared PostgreSQL schema bootstrap helpers.
    /// </summary>
    internal static PostgreSqlComponentSchemaDefinition Definition { get; } = new()
    {
        Component = PostgreSqlSchemaComponents.Inbox,
        CurrentSchemaVersion = PostgreSqlInboxSchema.CurrentSchemaVersion,
        VersionColumnSets = VersionColumnSets,
        SqlFiles = SqlFiles,
        BuildVersion1CreateScript = BuildVersion1CreateScript,
        BuildUpgradeScript = BuildUpgradeScript,
        BuildEnsureIndexesScript = BuildEnsureIndexesScript,
        BuildCreateScript = BuildCreateScript,
        CreateLockKey = CreateLockKey
    };

    /// <summary>
    ///     Builds the full create script for one inbox schema version, including metadata DDL.
    /// </summary>
    /// <param name="options">The store table and metadata options.</param>
    /// <param name="version">The target schema version.</param>
    /// <returns>The rendered create SQL batch.</returns>
    internal static string BuildCreateScript(IPostgreSqlStoreTableOptions options, int version)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (version <= 0 || version > PostgreSqlInboxSchema.CurrentSchemaVersion)
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
    ///     Builds the version 1 inbox create script with rendered identifier placeholders.
    /// </summary>
    /// <param name="options">The store table and metadata options.</param>
    /// <returns>The rendered version 1 create SQL batch.</returns>
    internal static string BuildVersion1CreateScript(IPostgreSqlStoreTableOptions options)
    {
        return PostgreSqlSqlScriptLoader.LoadAndRender(
            Assembly,
            PostgreSqlInboxSchemaEmbeddedSql.V1Create,
            CreateStoreTokens(options));
    }

    /// <summary>
    ///     Builds the incremental upgrade script between two adjacent inbox schema versions.
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
            _ => throw new ArgumentOutOfRangeException(nameof(toVersion), toVersion, "Unsupported inbox schema version.")
        };
    }

    /// <summary>
    ///     Builds the script that ensures inbox indexes exist for the current schema version.
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
        return tokens;
    }
}
