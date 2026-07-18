namespace LiteBus.Saga.Storage.PostgreSql;

/// <summary>
///     Embedded resource names for saga PostgreSQL schema SQL templates.
/// </summary>
internal static class PostgreSqlSagaSchemaEmbeddedSql
{
    /// <summary>
    ///     The embedded resource path for the version 1 saga create script.
    /// </summary>
    internal const string V1Create = "saga/v1/create.sql";

    /// <summary>
    ///     The embedded resource path for the version 1 saga index ensure script.
    /// </summary>
    internal const string V1EnsureIndexes = "saga/v1/ensure_indexes.sql";
}
