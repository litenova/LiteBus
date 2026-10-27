namespace LiteBus.Commands.Recurring.Storage.PostgreSql;

/// <summary>
///     Repository-relative paths to canonical SQL files shipped in <c>LiteBus.Commands.Recurring.Storage.PostgreSql</c>.
/// </summary>
/// <remarks>
///     Copy these files directly into Flyway, Liquibase, or DBA-owned migration folders. Replace
///     <c>{{TokenName}}</c> placeholders with quoted identifiers for your schema and table names, or call
///     <see cref="PostgreSqlRecurringLeaseSchema.GetCreateScript(PostgreSqlRecurringLeaseOptions?)" /> to render them.
/// </remarks>
public static class PostgreSqlRecurringLeaseSchemaSqlPaths
{
    /// <summary>
    ///     The repository-relative root folder for canonical SQL files in this package.
    /// </summary>
    private const string Root = "src/LiteBus.Commands.Recurring.Storage.PostgreSql/Sql/";

    /// <summary>
    ///     Creates the version 1 recurring leases table.
    /// </summary>
    public const string V1Create = Root + "recurring/v1/create.sql";

    /// <summary>
    ///     Ensures the current recurring lease indexes exist.
    /// </summary>
    public const string V1EnsureIndexes = Root + "recurring/v1/ensure_indexes.sql";
}
