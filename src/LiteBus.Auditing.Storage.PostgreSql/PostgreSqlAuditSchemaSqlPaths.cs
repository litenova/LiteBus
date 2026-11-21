namespace LiteBus.Auditing.Storage.PostgreSql;

/// <summary>
///     Repository-relative paths to canonical SQL files shipped in <c>LiteBus.Auditing.Storage.PostgreSql</c>.
/// </summary>
/// <remarks>
///     Copy these files directly into Flyway, Liquibase, or DBA-owned migration folders. Replace
///     <c>{{TokenName}}</c> placeholders with quoted identifiers for your schema and table names, or call
///     <see cref="PostgreSqlAuditSchema.GetCreateScript(PostgreSqlAuditStoreOptions?)" /> to render them.
/// </remarks>
public static class PostgreSqlAuditSchemaSqlPaths
{
    /// <summary>
    ///     The repository-relative root folder for canonical SQL files in this package.
    /// </summary>
    private const string Root = "src/LiteBus.Auditing.Storage.PostgreSql/Sql/";

    /// <summary>
    ///     Creates the version 1 audit records, heads, and checkpoints tables.
    /// </summary>
    public const string V1Create = Root + "audit/v1/create.sql";

    /// <summary>
    ///     Ensures the current audit indexes exist.
    /// </summary>
    public const string V1EnsureIndexes = Root + "audit/v1/ensure_indexes.sql";
}
