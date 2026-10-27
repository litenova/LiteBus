namespace LiteBus.Commands.Storage.PostgreSql;

/// <summary>
///     Repository-relative paths to canonical SQL files shipped in <c>LiteBus.Commands.Storage.PostgreSql</c>.
/// </summary>
/// <remarks>
///     Copy these files directly into Flyway, Liquibase, or DBA-owned migration folders. Replace
///     <c>{{TokenName}}</c> placeholders with quoted identifiers for your schema and table names, or call
///     <see cref="PostgreSqlIdempotencySchema.GetCreateScript(PostgreSqlIdempotencyStoreOptions?)" /> to render them.
/// </remarks>
public static class PostgreSqlIdempotencySchemaSqlPaths
{
    /// <summary>
    ///     The repository-relative root folder for canonical SQL files in this package.
    /// </summary>
    private const string Root = "src/LiteBus.Commands.Storage.PostgreSql/Sql/";

    /// <summary>
    ///     Creates the version 1 idempotency keys table.
    /// </summary>
    public const string V1Create = Root + "idempotency/v1/create.sql";

    /// <summary>
    ///     Ensures the current idempotency indexes exist.
    /// </summary>
    public const string V1EnsureIndexes = Root + "idempotency/v1/ensure_indexes.sql";
}
