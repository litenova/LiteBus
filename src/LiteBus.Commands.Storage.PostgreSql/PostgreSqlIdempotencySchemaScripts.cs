using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using LiteBus.Storage.PostgreSql;

namespace LiteBus.Commands.Storage.PostgreSql;

/// <summary>
///     Builds idempotency schema SQL scripts and supplies the component definition used by
///     <see cref="PostgreSqlSchemaManager" />.
/// </summary>
internal static class PostgreSqlIdempotencySchemaScripts
{
    /// <summary>
    ///     The assembly that embeds idempotency schema SQL resources.
    /// </summary>
    private static readonly Assembly Assembly = typeof(PostgreSqlIdempotencySchemaScripts).Assembly;

    /// <summary>
    ///     The column names required by idempotency schema version 1.
    /// </summary>
    internal static readonly IReadOnlyList<string> Version1Columns =
    [
        "idempotency_key",
        "payload",
        "fingerprint",
        "is_completed",
        "claimed_at",
        "completed_at"
    ];

    /// <summary>
    ///     The ordered column groups introduced by each idempotency schema version.
    /// </summary>
    internal static readonly IReadOnlyList<IReadOnlyList<string>> VersionColumnSets =
    [
        Version1Columns
    ];

    /// <summary>
    ///     The database column types required by the current idempotency schema.
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, string> RequiredColumnDataTypes =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["payload"] = "text",
            ["fingerprint"] = "text"
        };

    /// <summary>
    ///     Gets the canonical SQL files shipped with the idempotency PostgreSQL package.
    /// </summary>
    internal static IReadOnlyList<PostgreSqlSchemaSqlFile> SqlFiles { get; } =
    [
        new(
            PostgreSqlIdempotencySchemaSqlPaths.V1Create,
            "Creates the version 1 idempotency keys table with the key as primary key."),
        new(
            PostgreSqlIdempotencySchemaSqlPaths.V1EnsureIndexes,
            "Ensures the current idempotency indexes exist.")
    ];

    /// <summary>
    ///     Gets the schema definition consumed by shared PostgreSQL schema bootstrap helpers.
    /// </summary>
    internal static PostgreSqlComponentSchemaDefinition Definition { get; } = new()
    {
        Component = PostgreSqlIdempotencySchema.ComponentName,
        CurrentSchemaVersion = PostgreSqlIdempotencySchema.CurrentSchemaVersion,
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
    ///     Builds the full create script for the current idempotency schema, including metadata DDL.
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
    ///     Returns the rendered create script for the current idempotency schema.
    /// </summary>
    /// <param name="options">The idempotency store options.</param>
    /// <returns>The create script.</returns>
    internal static string GetCreateScript(PostgreSqlIdempotencyStoreOptions? options = null)
    {
        options ??= new PostgreSqlIdempotencyStoreOptions();
        return BuildCreateScript(options);
    }

    /// <summary>
    ///     Builds the baseline idempotency create script with rendered identifier placeholders.
    /// </summary>
    /// <param name="options">The store table and metadata options.</param>
    /// <returns>The rendered current-version create SQL batch.</returns>
    internal static string BuildBaselineCreateScript(IPostgreSqlStoreTableOptions options)
    {
        return PostgreSqlSqlScriptLoader.LoadAndRender(
            Assembly,
            PostgreSqlIdempotencySchemaEmbeddedSql.V1Create,
            CreateStoreTokens(options));
    }

    /// <summary>
    ///     Builds the script that ensures the current idempotency indexes exist.
    /// </summary>
    /// <param name="options">The store table and metadata options.</param>
    /// <returns>The rendered index ensure SQL batch.</returns>
    internal static string BuildEnsureIndexesScript(IPostgreSqlStoreTableOptions options)
    {
        return PostgreSqlSqlScriptLoader.LoadAndRender(
            Assembly,
            PostgreSqlIdempotencySchemaEmbeddedSql.V1EnsureIndexes,
            CreateStoreTokens(options));
    }

    /// <summary>
    ///     Returns the index names required for the current idempotency schema.
    /// </summary>
    /// <param name="options">The store table and metadata options.</param>
    /// <returns>The required index names for validation.</returns>
    internal static IReadOnlyList<string> GetRequiredIndexNames(IPostgreSqlStoreTableOptions options)
    {
        return
        [
            PostgreSqlIdentifier.UnquotedIndexName(options.TableName, "completed_idx")
        ];
    }

    /// <summary>
    ///     Creates the advisory lock key used during idempotency schema bootstrap.
    /// </summary>
    /// <param name="options">The store table options that identify the idempotency table.</param>
    /// <returns>The stable advisory lock key.</returns>
    internal static string CreateLockKey(IPostgreSqlStoreTableOptions options)
    {
        return $"litebus:{PostgreSqlIdempotencySchema.ComponentName}:{options.SchemaName}:{options.TableName}";
    }

    /// <summary>
    ///     Builds the placeholder token map used by idempotency SQL templates.
    /// </summary>
    /// <param name="options">The store table and metadata options.</param>
    /// <returns>The token map keyed by placeholder name without braces.</returns>
    private static Dictionary<string, string> CreateStoreTokens(IPostgreSqlStoreTableOptions options)
    {
        var tokens = PostgreSqlSchemaSqlTokens.ForStoreTable(options);
        tokens["CompletedIndexName"] = PostgreSqlIdentifier.IndexName(options.TableName, "completed_idx");
        return tokens;
    }
}
