namespace LiteBus.PostgreSql;

/// <summary>
///     Defines options for the LiteBus PostgreSQL schema version metadata table.
/// </summary>
/// <remarks>
///     The metadata table records which physical schema version was applied to each inbox or outbox table. Applications
///     can relocate the metadata table to a dedicated schema when DBAs require operational tables to stay separate from
///     application data.
/// </remarks>
public record PostgreSqlSchemaStoreOptions
{
    /// <summary>
    ///     Gets the PostgreSQL schema that stores LiteBus schema version metadata.
    /// </summary>
    public string MetadataSchemaName { get; init; } = "public";

    /// <summary>
    ///     Gets the PostgreSQL table that stores LiteBus schema version metadata.
    /// </summary>
    public string MetadataTableName { get; init; } = "litebus_schema_versions";

    /// <summary>
    ///     Gets the optional logger for schema creation, upgrade, and validation operations.
    /// </summary>
    /// <remarks>
    ///     When <see langword="null" />, schema operations run silently. Hosting adapters can bridge this interface to
    ///     application logging without adding logging package dependencies to <c>LiteBus.PostgreSql</c>.
    /// </remarks>
    public IPostgreSqlSchemaLogger? Logger { get; init; }
}
