using System.Reflection;
using System.Text;
using LiteBus.Storage.PostgreSql;

namespace LiteBus.Saga.Storage.PostgreSql;

/// <summary>
///     Builds saga schema SQL scripts and supplies the component definition used by
///     <see cref="PostgreSqlSchemaManager" />.
/// </summary>
internal static class PostgreSqlSagaSchemaScripts
{
    /// <summary>
    ///     The assembly that embeds saga schema SQL resources.
    /// </summary>
    private static readonly Assembly Assembly = typeof(PostgreSqlSagaSchemaScripts).Assembly;

    /// <summary>
    ///     The column names required by saga schema version 1.
    /// </summary>
    internal static readonly IReadOnlyList<string> Version1Columns =
    [
        "correlation_id",
        "saga_type",
        "tenant_id",
        "state_json",
        "optimistic_lock_version",
        "is_completed",
        "last_applied_message_id",
        "created_at",
        "updated_at"
    ];

    /// <summary>
    ///     The ordered column groups introduced by each saga schema version.
    /// </summary>
    internal static readonly IReadOnlyList<IReadOnlyList<string>> VersionColumnSets =
    [
        Version1Columns
    ];

    /// <summary>
    ///     The database column types required by the current saga schema.
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, string> RequiredColumnDataTypes =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["state_json"] = "jsonb",
            ["last_applied_message_id"] = "uuid"
        };

    /// <summary>
    ///     Gets the canonical SQL files shipped with the saga PostgreSQL package.
    /// </summary>
    internal static IReadOnlyList<PostgreSqlSchemaSqlFile> SqlFiles { get; } =
    [
        new(
            PostgreSqlSagaSchemaSqlPaths.V1Create,
            "Creates the version 1 saga instances table with tenant-scoped primary key."),
        new(
            PostgreSqlSagaSchemaSqlPaths.V1EnsureIndexes,
            "Ensures the current saga indexes exist.")
    ];

    /// <summary>
    ///     Gets the schema definition consumed by shared PostgreSQL schema bootstrap helpers.
    /// </summary>
    internal static PostgreSqlComponentSchemaDefinition Definition { get; } = new()
    {
        Component = PostgreSqlSchemaComponents.Saga,
        CurrentSchemaVersion = PostgreSqlSagaSchema.CurrentSchemaVersion,
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
    ///     Builds the full create script for the current saga schema, including metadata DDL.
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
    ///     Returns the rendered create script for the current saga schema.
    /// </summary>
    /// <param name="options">The saga store options.</param>
    /// <returns>The create script.</returns>
    internal static string GetCreateScript(PostgreSqlSagaStoreOptions? options = null)
    {
        options ??= new PostgreSqlSagaStoreOptions();
        return BuildCreateScript(options);
    }

    /// <summary>
    ///     Builds the baseline saga create script with rendered identifier placeholders.
    /// </summary>
    /// <param name="options">The store table and metadata options.</param>
    /// <returns>The rendered current-version create SQL batch.</returns>
    internal static string BuildBaselineCreateScript(IPostgreSqlStoreTableOptions options)
    {
        return PostgreSqlSqlScriptLoader.LoadAndRender(
            Assembly,
            PostgreSqlSagaSchemaEmbeddedSql.V1Create,
            CreateStoreTokens(options));
    }

    /// <summary>
    ///     Builds the script that ensures the current saga indexes exist.
    /// </summary>
    /// <param name="options">The store table and metadata options.</param>
    /// <returns>The rendered index ensure SQL batch.</returns>
    internal static string BuildEnsureIndexesScript(IPostgreSqlStoreTableOptions options)
    {
        return PostgreSqlSqlScriptLoader.LoadAndRender(
            Assembly,
            PostgreSqlSagaSchemaEmbeddedSql.V1EnsureIndexes,
            CreateStoreTokens(options));
    }

    /// <summary>
    ///     Returns the index names required for the current saga schema.
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
    ///     Creates the advisory lock key used during saga schema bootstrap.
    /// </summary>
    /// <param name="options">The store table options that identify the saga table.</param>
    /// <returns>The stable advisory lock key.</returns>
    internal static string CreateLockKey(IPostgreSqlStoreTableOptions options)
    {
        return $"litebus:{PostgreSqlSchemaComponents.Saga}:{options.SchemaName}:{options.TableName}";
    }

    /// <summary>
    ///     Builds the placeholder token map used by saga SQL templates.
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
