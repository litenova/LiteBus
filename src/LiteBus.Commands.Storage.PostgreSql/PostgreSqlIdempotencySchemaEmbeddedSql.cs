namespace LiteBus.Commands.Storage.PostgreSql;

/// <summary>
///     Embedded resource names for idempotency PostgreSQL schema SQL templates.
/// </summary>
internal static class PostgreSqlIdempotencySchemaEmbeddedSql
{
    /// <summary>
    ///     The embedded resource path for the version 1 idempotency create script.
    /// </summary>
    internal const string V1Create = "idempotency/v1/create.sql";

    /// <summary>
    ///     The embedded resource path for the version 1 idempotency index ensure script.
    /// </summary>
    internal const string V1EnsureIndexes = "idempotency/v1/ensure_indexes.sql";
}
