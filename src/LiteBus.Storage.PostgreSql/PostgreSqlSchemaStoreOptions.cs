namespace LiteBus.Storage.PostgreSql;

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
    ///     application logging without adding logging package dependencies to <c>LiteBus.Storage.PostgreSql</c>.
    /// </remarks>
    public IPostgreSqlSchemaLogger? Logger { get; init; }

    /// <summary>
    ///     Gets a value indicating whether <see cref="PostgreSqlSchemaManager.ValidateAsync" /> should verify required
    ///     indexes exist on the store table.
    /// </summary>
    /// <remarks>
    ///     When <see langword="true" />, validation fails with <see cref="PostgreSqlSchemaDriftException" /> when an
    ///     expected index is missing. Set to <see langword="false" /> only when an external migration tool manages indexes
    ///     separately and startup should check columns and metadata only.
    /// </remarks>
    public bool ValidateIndexesOnStartup { get; init; } = true;
}
