namespace LiteBus.Inbox.Storage.PostgreSql;

/// <summary>
///     Embedded SQL resource paths used by <see cref="PostgreSqlInboxSchemaScripts" /> at runtime.
/// </summary>
/// <remarks>
///     These paths map to manifest resource names such as
///     <c>LiteBus.Inbox.Storage.PostgreSql.Sql.inbox.v1.create.sql</c>. For copy-paste migration ownership, use the public
///     repository or NuGet paths from <see cref="PostgreSqlInboxSchemaSqlPaths" /> instead.
/// </remarks>
internal static class PostgreSqlInboxSchemaEmbeddedSql
{
    /// <summary>
    ///     Embedded resource path for version 1 inbox table creation SQL.
    /// </summary>
    internal const string V1Create = "inbox/v1/create.sql";

    /// <summary>
    ///     Embedded resource path for inbox index ensure SQL.
    /// </summary>
    internal const string V1EnsureIndexes = "inbox/v1/ensure_indexes.sql";
}
