namespace LiteBus.Outbox.Storage.PostgreSql;

/// <summary>
///     Embedded SQL resource paths used by <see cref="PostgreSqlOutboxSchemaScripts" /> at runtime.
/// </summary>
/// <remarks>
///     These paths map to manifest resource names such as
///     <c>LiteBus.Outbox.Storage.PostgreSql.Sql.outbox.v1.create.sql</c>. For copy-paste migration ownership, use the public
///     repository or NuGet paths from <see cref="PostgreSqlOutboxSchemaSqlPaths" /> instead.
/// </remarks>
internal static class PostgreSqlOutboxSchemaEmbeddedSql
{
    /// <summary>
    ///     Embedded resource path for version 1 outbox table creation SQL.
    /// </summary>
    internal const string V1Create = "outbox/v1/create.sql";

    /// <summary>
    ///     Embedded resource path for outbox index ensure SQL.
    /// </summary>
    internal const string V1EnsureIndexes = "outbox/v1/ensure_indexes.sql";
}
