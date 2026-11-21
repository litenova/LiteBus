using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using LiteBus.Storage.PostgreSql;

namespace LiteBus.Commands.Recurring.Storage.PostgreSql;

/// <summary>
///     Builds recurring occurrence schema SQL scripts and supplies the component definition used by
///     <see cref="PostgreSqlSchemaManager" />.
/// </summary>
internal static class PostgreSqlRecurringOccurrenceSchemaScripts
{
    /// <summary>
    ///     The assembly that embeds recurring occurrence schema SQL resources.
    /// </summary>
    private static readonly Assembly Assembly = typeof(PostgreSqlRecurringOccurrenceSchemaScripts).Assembly;

    /// <summary>
    ///     The column names version 1 introduced on the occurrence table.
    /// </summary>
    /// <remarks>
    ///     Version 1 of the recurring schema shipped only the leases table, so the occurrence table has no version 1
    ///     columns. The empty set keeps version inference honest: a table carrying the version 2 columns infers version
    ///     2, and a table missing any of them infers version 1 and fails bootstrap with a drift error.
    /// </remarks>
    internal static readonly IReadOnlyList<string> Version1Columns = [];

    /// <summary>
    ///     The column names introduced by recurring occurrence schema version 2.
    /// </summary>
    internal static readonly IReadOnlyList<string> Version2Columns =
    [
        "occurrence_key",
        "loop_name",
        "tenant_id",
        "scheduled_at",
        "recorded_at"
    ];

    /// <summary>
    ///     The ordered column groups introduced by each recurring occurrence schema version.
    /// </summary>
    internal static readonly IReadOnlyList<IReadOnlyList<string>> VersionColumnSets =
    [
        Version1Columns,
        Version2Columns
    ];

    /// <summary>
    ///     The database column types required by the current recurring occurrence schema.
    /// </summary>
    /// <remarks>
    ///     The key stays <c>text</c> so occurrence keys are never truncated, and both instants stay
    ///     <c>timestamp with time zone</c> so a <c>timestamp without time zone</c> column cannot silently shift the
    ///     scheduled instants catch-up decisions compare. Names follow <c>information_schema.columns.data_type</c>.
    /// </remarks>
    internal static readonly IReadOnlyDictionary<string, string> RequiredColumnDataTypes =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["occurrence_key"] = "text",
            ["scheduled_at"] = "timestamp with time zone",
            ["recorded_at"] = "timestamp with time zone"
        };

    /// <summary>
    ///     Gets the canonical occurrence SQL files shipped with the recurring PostgreSQL package.
    /// </summary>
    internal static IReadOnlyList<PostgreSqlSchemaSqlFile> SqlFiles { get; } =
    [
        new(
            PostgreSqlRecurringOccurrenceSchemaSqlPaths.V2Create,
            "Creates the version 2 recurring occurrences table with the occurrence key as primary key."),
        new(
            PostgreSqlRecurringOccurrenceSchemaSqlPaths.V2EnsureIndexes,
            "Ensures the current recurring occurrence indexes exist.")
    ];

    /// <summary>
    ///     Gets the schema definition consumed by shared PostgreSQL schema bootstrap helpers.
    /// </summary>
    /// <remarks>
    ///     The definition carries no upgrade scripts: the occurrence table is new in version 2, so there is no version 1
    ///     occurrence table to migrate. Deployments upgrading from version 1 create the table through the baseline
    ///     create script during ensure.
    /// </remarks>
    internal static PostgreSqlComponentSchemaDefinition Definition { get; } = new()
    {
        Component = PostgreSqlRecurringOccurrenceSchema.ComponentName,
        CurrentSchemaVersion = PostgreSqlRecurringOccurrenceSchema.CurrentSchemaVersion,
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
    ///     Builds the full create script for the current recurring occurrence schema, including metadata DDL.
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
    ///     Builds the baseline recurring occurrence create script with rendered identifier placeholders.
    /// </summary>
    /// <param name="options">The store table and metadata options.</param>
    /// <returns>The rendered current-version create SQL batch.</returns>
    internal static string BuildBaselineCreateScript(IPostgreSqlStoreTableOptions options)
    {
        return PostgreSqlSqlScriptLoader.LoadAndRender(
            Assembly,
            PostgreSqlRecurringOccurrenceSchemaEmbeddedSql.V2Create,
            CreateStoreTokens(options));
    }

    /// <summary>
    ///     Builds the script that ensures the current recurring occurrence indexes exist.
    /// </summary>
    /// <param name="options">The store table and metadata options.</param>
    /// <returns>The rendered index ensure SQL batch.</returns>
    internal static string BuildEnsureIndexesScript(IPostgreSqlStoreTableOptions options)
    {
        return PostgreSqlSqlScriptLoader.LoadAndRender(
            Assembly,
            PostgreSqlRecurringOccurrenceSchemaEmbeddedSql.V2EnsureIndexes,
            CreateStoreTokens(options));
    }

    /// <summary>
    ///     Returns the index names required for the current recurring occurrence schema.
    /// </summary>
    /// <param name="options">The store table and metadata options.</param>
    /// <returns>The required index names for validation.</returns>
    internal static IReadOnlyList<string> GetRequiredIndexNames(IPostgreSqlStoreTableOptions options)
    {
        return
        [
            PostgreSqlIdentifier.UnquotedIndexName(options.TableName, "lookup_idx")
        ];
    }

    /// <summary>
    ///     Creates the advisory lock key used during recurring occurrence schema bootstrap.
    /// </summary>
    /// <param name="options">The store table options that identify the recurring occurrences table.</param>
    /// <returns>The stable advisory lock key.</returns>
    internal static string CreateLockKey(IPostgreSqlStoreTableOptions options)
    {
        return $"litebus:{PostgreSqlRecurringOccurrenceSchema.ComponentName}:{options.SchemaName}:{options.TableName}";
    }

    /// <summary>
    ///     Builds the placeholder token map used by recurring occurrence SQL templates.
    /// </summary>
    /// <param name="options">The store table and metadata options.</param>
    /// <returns>The token map keyed by placeholder name without braces.</returns>
    private static Dictionary<string, string> CreateStoreTokens(IPostgreSqlStoreTableOptions options)
    {
        var tokens = PostgreSqlSchemaSqlTokens.ForStoreTable(options);
        tokens["QualifiedOccurrenceTableName"] = PostgreSqlIdentifier.Qualify(options.SchemaName, options.TableName);
        tokens["OccurrenceLookupIndexName"] = PostgreSqlIdentifier.IndexName(options.TableName, "lookup_idx");
        return tokens;
    }
}
