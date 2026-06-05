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
    public const string V2Upgrade = "src/LiteBus.Storage.PostgreSql/Sql/shared/add_trace_context_column.sql";

    /// <summary>
    ///     Shared version 3 upgrade that adds <c>idempotency_key</c> and its unique partial index.
    /// </summary>
    public const string V3Upgrade = "src/LiteBus.Storage.PostgreSql/Sql/shared/add_idempotency_key_column.sql";

    /// <summary>
    ///     Upgrades the outbox table from version 3 to version 4 (insert notify trigger).
    /// </summary>
    public const string V4Upgrade = Root + "outbox/v4/add_insert_notify.sql";

    /// <summary>
    ///     Upgrades the outbox table from version 4 to version 5 (published_at column).
    /// </summary>
    public const string V5Upgrade = Root + "outbox/v5/add_published_at.sql";
}
