namespace LiteBus.Outbox.Storage.PostgreSql;

/// <summary>
///     Repository-relative paths to canonical SQL files shipped in <c>LiteBus.Outbox.Storage.PostgreSql</c>.
/// </summary>
/// <remarks>
///     Copy these files directly into Flyway, Liquibase, or DBA-owned migration folders. Replace
///     <c>{{TokenName}}</c> placeholders with quoted identifiers for your schema and table names, or call
///     <see cref="PostgreSqlOutboxSchema.GetCreateScript(PostgreSqlOutboxStoreOptions?)" /> to render them.
/// </remarks>
public static class PostgreSqlOutboxSchemaSqlPaths
{
    /// <summary>
    ///     The repository-relative root folder for canonical SQL files in this package.
    /// </summary>
    private const string Root = "src/LiteBus.Outbox.Storage.PostgreSql/Sql/";

    /// <summary>
    ///     Creates the version 1 outbox table, indexes, and insert notify trigger.
    /// </summary>
    public const string V1Create = Root + "outbox/v1/create.sql";

    /// <summary>
    ///     Ensures outbox indexes exist for schema version 1.
    /// </summary>
    public const string V1EnsureIndexes = Root + "outbox/v1/ensure_indexes.sql";
}