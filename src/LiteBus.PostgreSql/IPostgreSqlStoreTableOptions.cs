namespace LiteBus.PostgreSql;

/// <summary>
///     Identifies a PostgreSQL store table and the schema metadata options used during bootstrap.
/// </summary>
public interface IPostgreSqlStoreTableOptions
{
    /// <summary>
    ///     Gets the PostgreSQL schema name that stores inbox or outbox rows.
    /// </summary>
    string SchemaName { get; }

    /// <summary>
    ///     Gets the PostgreSQL table name that stores inbox or outbox rows.
    /// </summary>
    string TableName { get; }

    /// <summary>
    ///     Gets the PostgreSQL schema that stores LiteBus schema version metadata.
    /// </summary>
    string MetadataSchemaName { get; }

    /// <summary>
    ///     Gets the PostgreSQL table that stores LiteBus schema version metadata.
    /// </summary>
    string MetadataTableName { get; }

    /// <summary>
    ///     Gets the optional logger for schema operations.
    /// </summary>
    IPostgreSqlSchemaLogger? Logger { get; }
}
