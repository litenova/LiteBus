namespace LiteBus.Commands.Recurring.Storage.PostgreSql;

/// <summary>
///     Embedded resource names for recurring lease PostgreSQL schema SQL templates.
/// </summary>
internal static class PostgreSqlRecurringLeaseSchemaEmbeddedSql
{
    /// <summary>
    ///     The embedded resource path for the version 1 recurring lease create script.
    /// </summary>
    internal const string V1Create = "recurring/v1/create.sql";

    /// <summary>
    ///     The embedded resource path for the version 1 recurring lease index ensure script.
    /// </summary>
    internal const string V1EnsureIndexes = "recurring/v1/ensure_indexes.sql";
}
