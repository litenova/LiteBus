using System.Collections.Generic;

namespace LiteBus.Storage.PostgreSql;

/// <summary>
///     Repository-relative paths to canonical SQL files shipped in <c>LiteBus.Storage.PostgreSql</c>.
/// </summary>
/// <remarks>
///     Copy these files directly into Flyway, Liquibase, or DBA-owned migration folders. Replace
///     <c>{{TokenName}}</c> placeholders with quoted identifiers for your schema and table names, or call
///     <see cref="PostgreSqlSchemaVersionStore.GetMetadataCreateScript(IPostgreSqlStoreTableOptions)" /> to render
///     metadata DDL.
/// </remarks>
public static class PostgreSqlSchemaSqlPaths
{
    /// <summary>
    ///     The repository-relative root folder for canonical SQL files in this package.
    /// </summary>
    private const string Root = "src/LiteBus.Storage.PostgreSql/Sql/";

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
    ///     Checks whether one index exists on a table.
    /// </summary>
    public const string InspectorIndexExists = Root + "inspector/index_exists.sql";

    /// <summary>
    ///     Gets the canonical SQL files shipped in this package.
    /// </summary>
    public static IReadOnlyList<PostgreSqlSchemaSqlFile> Files { get; } =
    [
        new(MetadataCreate, "Creates the LiteBus schema version metadata table."),
        new(MetadataSelectVersion, "Reads one schema version row. Used internally at runtime."),
        new(MetadataUpsertVersion, "Writes one schema version row. Used internally at runtime."),
        new(InspectorTableExists, "Checks whether a table exists. Used internally at runtime."),
        new(InspectorListColumns, "Lists table columns. Used internally at runtime."),
        new(InspectorIndexExists, "Checks whether one index exists. Used internally at runtime.")
    ];
}