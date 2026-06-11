using System;

namespace LiteBus.Storage.PostgreSql;

/// <summary>
///     Identifies one PostgreSQL schema and table pair used by LiteBus store or metadata operations.
/// </summary>
/// <remarks>
///     Schema helpers pass this value object instead of separate <c>schemaName</c> and <c>tableName</c> parameters so
///     logging, metadata lookups, and inspection calls share one representation of a physical table.
/// </remarks>
public sealed record PostgreSqlTableReference
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="PostgreSqlTableReference" /> class.
    /// </summary>
    /// <param name="schemaName">The unquoted PostgreSQL schema name.</param>
    /// <param name="tableName">The unquoted PostgreSQL table name.</param>
    public PostgreSqlTableReference(string schemaName, string tableName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(schemaName);
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);

        SchemaName = schemaName;
        TableName = tableName;
    }

    /// <summary>
    ///     Gets the unquoted PostgreSQL schema name.
    /// </summary>
    public string SchemaName { get; }

    /// <summary>
    ///     Gets the unquoted PostgreSQL table name.
    /// </summary>
    public string TableName { get; }

    /// <summary>
    ///     Gets the qualified table name in the form <c>schema.table</c> for logging and error messages.
    /// </summary>
    public string QualifiedName => $"{SchemaName}.{TableName}";

    /// <summary>
    ///     Gets the quoted qualified identifier in the form <c>"schema"."table"</c>.
    /// </summary>
    public string QuotedQualifiedName => PostgreSqlIdentifier.Qualify(SchemaName, TableName);

    /// <summary>
    ///     Creates a reference to the inbox or outbox store table described by <paramref name="options" />.
    /// </summary>
    /// <param name="options">The store table and metadata options.</param>
    /// <returns>A table reference for the physical store table.</returns>
    public static PostgreSqlTableReference ForStore(IPostgreSqlStoreTableOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return new PostgreSqlTableReference(options.SchemaName, options.TableName);
    }

    /// <summary>
    ///     Creates a reference to the LiteBus schema version metadata table described by <paramref name="options" />.
    /// </summary>
    /// <param name="options">The store table and metadata options.</param>
    /// <returns>A table reference for the schema metadata table.</returns>
    public static PostgreSqlTableReference ForMetadata(IPostgreSqlStoreTableOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return new PostgreSqlTableReference(options.MetadataSchemaName, options.MetadataTableName);
    }

    /// <summary>
    ///     Creates a table reference from explicit schema and table names.
    /// </summary>
    /// <param name="schemaName">The unquoted PostgreSQL schema name.</param>
    /// <param name="tableName">The unquoted PostgreSQL table name.</param>
    /// <returns>The table reference.</returns>
    public static PostgreSqlTableReference Create(string schemaName, string tableName)
    {
        return new PostgreSqlTableReference(schemaName, tableName);
    }
}