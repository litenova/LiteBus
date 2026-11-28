namespace LiteBus.Commands.Recurring.Storage.PostgreSql;

/// <summary>
///     Embedded resource names for recurring occurrence PostgreSQL schema SQL templates.
/// </summary>
internal static class PostgreSqlRecurringOccurrenceSchemaEmbeddedSql
{
    /// <summary>
    ///     The embedded resource path for the version 2 recurring occurrence create script.
    /// </summary>
    internal const string V2Create = "recurring/v2/create.sql";

    /// <summary>
    ///     The embedded resource path for the version 2 recurring occurrence index ensure script.
    /// </summary>
    internal const string V2EnsureIndexes = "recurring/v2/ensure_indexes.sql";
}
