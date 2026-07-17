namespace LiteBus.Saga.Storage.PostgreSql;

/// <summary>
///     Repository-relative paths to canonical SQL files shipped in <c>LiteBus.Saga.Storage.PostgreSql</c>.
/// </summary>
/// <remarks>
///     Copy these files directly into Flyway, Liquibase, or DBA-owned migration folders. Replace
///     <c>{{TokenName}}</c> placeholders with quoted identifiers for your schema and table names, or call
///     <see cref="PostgreSqlSagaSchema.GetCreateScript(PostgreSqlSagaStoreOptions?)" /> to render them.
/// </remarks>
public static class PostgreSqlSagaSchemaSqlPaths
{
    /// <summary>
    ///     The repository-relative root folder for canonical SQL files in this package.
    /// </summary>
    private const string Root = "src/LiteBus.Saga.Storage.PostgreSql/Sql/";

    /// <summary>
    ///     Creates the version 1 saga instances table.
    /// </summary>
    public const string V1Create = Root + "saga/v1/create.sql";

    /// <summary>
    ///     Ensures the current saga indexes exist.
    /// </summary>
    public const string V1EnsureIndexes = Root + "saga/v1/ensure_indexes.sql";

    /// <summary>
    ///     Adds the applied message identifier used to suppress duplicate saga dispatches.
    /// </summary>
    public const string V2AddLastAppliedMessageId = Root + "saga/v2/add_last_applied_message_id.sql";
}
