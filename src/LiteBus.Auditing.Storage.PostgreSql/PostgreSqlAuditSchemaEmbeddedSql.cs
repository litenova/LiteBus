namespace LiteBus.Auditing.Storage.PostgreSql;

/// <summary>
///     Embedded resource names for audit PostgreSQL schema SQL templates.
/// </summary>
internal static class PostgreSqlAuditSchemaEmbeddedSql
{
    /// <summary>
    ///     The embedded resource path for the version 1 audit create script.
    /// </summary>
    internal const string V1Create = "audit/v1/create.sql";

    /// <summary>
    ///     The embedded resource path for the version 1 audit index ensure script.
    /// </summary>
    internal const string V1EnsureIndexes = "audit/v1/ensure_indexes.sql";
}
