using System.Collections.Generic;

namespace LiteBus.PostgreSql;

/// <summary>
///     Repository-relative paths to canonical SQL files shipped in <c>LiteBus.PostgreSql</c>.
/// </summary>
/// <remarks>
///     Copy these files directly into Flyway, Liquibase, or DBA-owned migration folders. Replace
///     <c>{{TokenName}}</c> placeholders with quoted identifiers for your schema and table names, or call
///     <see cref="PostgreSqlSchemaVersionStore.GetMetadataCreateScript(IPostgreSqlStoreTableOptions)" /> to render
///     metadata DDL.
/// </remarks>
public static class PostgreSqlSchemaSqlPaths
{
    private const string Root = "src/LiteBus.PostgreSql/Sql/";

    /// <summary>
    ///     Creates the schema version metadata table.
    /// </summary>
    public const string MetadataCreate = Root + "metadata/create.sql";

    /// <summary>
    ///     Reads one recorded schema version row.
    /// </summary>
    public const string MetadataSelectVersion = Root + "metadata/select_version.sql";

    /// <summary>
    ///     Inserts or updates one recorded schema version row.
    /// </summary>
    public const string MetadataUpsertVersion = Root + "metadata/upsert_version.sql";

    /// <summary>
    ///     Checks whether a base table exists.
    /// </summary>
    public const string InspectorTableExists = Root + "inspector/table_exists.sql";

    /// <summary>
    ///     Lists columns for one table.
    /// </summary>
    public const string InspectorListColumns = Root + "inspector/list_columns.sql";

    /// <summary>
    ///     Adds the shared version 2 <c>trace_context</c> column.
    /// </summary>
    public const string SharedAddTraceContextColumn = Root + "shared/add_trace_context_column.sql";

    /// <summary>
    ///     Gets the canonical SQL files shipped in this package.
    /// </summary>
    public static IReadOnlyList<PostgreSqlSchemaSqlFile> Files { get; } =
    [
        new PostgreSqlSchemaSqlFile(MetadataCreate, "Creates the LiteBus schema version metadata table."),
        new PostgreSqlSchemaSqlFile(MetadataSelectVersion, "Reads one schema version row. Used internally at runtime."),
        new PostgreSqlSchemaSqlFile(MetadataUpsertVersion, "Writes one schema version row. Used internally at runtime."),
        new PostgreSqlSchemaSqlFile(InspectorTableExists, "Checks whether a table exists. Used internally at runtime."),
        new PostgreSqlSchemaSqlFile(InspectorListColumns, "Lists table columns. Used internally at runtime."),
        new PostgreSqlSchemaSqlFile(
            SharedAddTraceContextColumn,
            "Version 2 upgrade that adds nullable trace_context jsonb to inbox or outbox tables.")
    ];
}

/// <summary>
///     Embedded SQL resource paths used by the shared PostgreSQL schema loader.
/// </summary>
internal static class PostgreSqlSchemaEmbeddedSql
{
    internal const string MetadataCreate = "metadata/create.sql";
    internal const string MetadataSelectVersion = "metadata/select_version.sql";
    internal const string MetadataUpsertVersion = "metadata/upsert_version.sql";
    internal const string InspectorTableExists = "inspector/table_exists.sql";
    internal const string InspectorListColumns = "inspector/list_columns.sql";
    internal const string SharedAddTraceContextColumn = "shared/add_trace_context_column.sql";
}
