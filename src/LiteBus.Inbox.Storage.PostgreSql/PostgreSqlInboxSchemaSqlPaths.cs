namespace LiteBus.Inbox.Storage.PostgreSql;

/// <summary>
///     Repository-relative paths to canonical SQL files shipped in <c>LiteBus.Inbox.Storage.PostgreSql</c>.
/// </summary>
/// <remarks>
///     Copy these files directly into Flyway, Liquibase, or DBA-owned migration folders. Replace
///     <c>{{TokenName}}</c> placeholders with quoted identifiers for your schema and table names, or call
///     <see cref="PostgreSqlInboxSchema.GetCreateScript(PostgreSqlInboxStoreOptions?)" /> to render them.
/// </remarks>
public static class PostgreSqlInboxSchemaSqlPaths
{
    /// <summary>
    ///     The repository-relative root folder for canonical SQL files in this package.
    /// </summary>
    private const string Root = "src/LiteBus.Inbox.Storage.PostgreSql/Sql/";

    /// <summary>
    ///     Creates the version 1 inbox table, indexes, and insert notify trigger.
    /// </summary>
    public const string V1Create = Root + "inbox/v1/create.sql";

    /// <summary>
    ///     Ensures inbox indexes exist for schema version 1.
    /// </summary>
    public const string V1EnsureIndexes = Root + "inbox/v1/ensure_indexes.sql";
}
