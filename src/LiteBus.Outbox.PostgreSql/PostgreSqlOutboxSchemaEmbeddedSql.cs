namespace LiteBus.Outbox.PostgreSql;

/// <summary>
///     Embedded SQL resource paths used by <see cref="PostgreSqlOutboxSchemaScripts" /> at runtime.
/// </summary>
/// <remarks>
///     These paths map to manifest resource names such as
///     <c>LiteBus.Outbox.PostgreSql.Sql.outbox.v1.create.sql</c>. For copy-paste migration ownership, use the public
///     repository or NuGet paths from <see cref="PostgreSqlOutboxSchemaSqlPaths" /> instead.
/// </remarks>
internal static class PostgreSqlOutboxSchemaEmbeddedSql
{
    internal const string V1Create = "outbox/v1/create.sql";
    internal const string V1EnsureIndexes = "outbox/v1/ensure_indexes.sql";
}
