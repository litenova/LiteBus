namespace LiteBus.Outbox.PostgreSql;

/// <summary>
///     Repository-relative paths to canonical SQL files shipped in <c>LiteBus.Outbox.PostgreSql</c>.
/// </summary>
/// <remarks>
///     Copy these files directly into Flyway, Liquibase, or DBA-owned migration folders. Replace
///     <c>{{TokenName}}</c> placeholders with quoted identifiers for your schema and table names, or call
///     <see cref="PostgreSqlOutboxSchema.GetCreateScript(PostgreSqlOutboxStoreOptions?)" /> to render them.
/// </remarks>
public static class PostgreSqlOutboxSchemaSqlPaths
{
    private const string Root = "src/LiteBus.Outbox.PostgreSql/Sql/";

    /// <summary>
    ///     Creates the version 1 outbox table and indexes.
    /// </summary>
    public const string V1Create = Root + "outbox/v1/create.sql";

    /// <summary>
    ///     Ensures outbox indexes exist for the current schema version.
    /// </summary>
    public const string V1EnsureIndexes = Root + "outbox/v1/ensure_indexes.sql";

    /// <summary>
    ///     Shared version 2 upgrade that adds <c>trace_context</c>.
    /// </summary>
    public const string V2Upgrade = "src/LiteBus.PostgreSql/Sql/shared/add_trace_context_column.sql";
}
