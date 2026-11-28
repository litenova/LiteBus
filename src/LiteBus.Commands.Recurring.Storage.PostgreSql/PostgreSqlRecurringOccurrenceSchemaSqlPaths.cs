namespace LiteBus.Commands.Recurring.Storage.PostgreSql;

/// <summary>
///     Repository-relative paths to the occurrence SQL files shipped in <c>LiteBus.Commands.Recurring.Storage.PostgreSql</c>.
/// </summary>
/// <remarks>
///     Copy these files directly into Flyway, Liquibase, or DBA-owned migration folders. Replace
///     <c>{{TokenName}}</c> placeholders with quoted identifiers for your schema and table names, or call
///     <see cref="PostgreSqlRecurringOccurrenceSchema.GetCreateScript(PostgreSqlRecurringLeaseOptions?)" /> to render them.
/// </remarks>
public static class PostgreSqlRecurringOccurrenceSchemaSqlPaths
{
    /// <summary>
    ///     The repository-relative root folder for canonical SQL files in this package.
    /// </summary>
    private const string Root = "src/LiteBus.Commands.Recurring.Storage.PostgreSql/Sql/";

    /// <summary>
    ///     Creates the version 2 recurring occurrences table.
    /// </summary>
    public const string V2Create = Root + "recurring/v2/create.sql";

    /// <summary>
    ///     Ensures the current recurring occurrence indexes exist.
    /// </summary>
    public const string V2EnsureIndexes = Root + "recurring/v2/ensure_indexes.sql";
}
