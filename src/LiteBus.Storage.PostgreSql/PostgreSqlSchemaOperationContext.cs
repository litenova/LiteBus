using System;
using Npgsql;

namespace LiteBus.Storage.PostgreSql;

/// <summary>
///     Bundles the connection, options, component metadata, table references, and logger used by schema operations.
/// </summary>
/// <remarks>
///     Schema manager and version store methods previously repeated <c>connection</c>, <c>options</c>,
///     <c>component</c>, <c>schemaName</c>, <c>tableName</c>, and <c>logger</c> parameters. This context keeps those
///     values together for one store table operation.
/// </remarks>
internal sealed class PostgreSqlSchemaOperationContext
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="PostgreSqlSchemaOperationContext" /> class.
    /// </summary>
    /// <param name="connection">The open PostgreSQL connection.</param>
    /// <param name="options">The store table and metadata options.</param>
    /// <param name="component">The LiteBus store component name.</param>
    /// <param name="storeTable">The physical store table under operation.</param>
    /// <param name="metadataTable">The LiteBus schema version metadata table.</param>
    /// <param name="definition">The component schema definition that supplies version metadata, when present.</param>
    /// <param name="logger">The schema logger that receives operational output.</param>
    private PostgreSqlSchemaOperationContext(
        NpgsqlConnection connection,
        IPostgreSqlStoreTableOptions options,
        string component,
        PostgreSqlTableReference storeTable,
        PostgreSqlTableReference metadataTable,
        PostgreSqlComponentSchemaDefinition? definition,
        IPostgreSqlSchemaLogger logger)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(component);
        ArgumentNullException.ThrowIfNull(logger);

        Connection = connection;
        Options = options;
        Component = component;
        StoreTable = storeTable;
        MetadataTable = metadataTable;
        Definition = definition;
        Logger = logger;
    }

    /// <summary>
    ///     Gets the open PostgreSQL connection.
    /// </summary>
    public NpgsqlConnection Connection { get; }

    /// <summary>
    ///     Gets the store table and metadata options.
    /// </summary>
    public IPostgreSqlStoreTableOptions Options { get; }

    /// <summary>
    ///     Gets the component schema definition that supplies version metadata, when present.
    /// </summary>
    public PostgreSqlComponentSchemaDefinition? Definition { get; }

    /// <summary>
    ///     Gets the LiteBus store component name.
    /// </summary>
    public string Component { get; }

    /// <summary>
    ///     Gets the physical store table under operation.
    /// </summary>
    public PostgreSqlTableReference StoreTable { get; }

    /// <summary>
    ///     Gets the LiteBus schema version metadata table.
    /// </summary>
    public PostgreSqlTableReference MetadataTable { get; }

    /// <summary>
    ///     Gets the schema logger that receives operational output.
    /// </summary>
    public IPostgreSqlSchemaLogger Logger { get; }

    /// <summary>
    ///     Creates a schema operation context from store table options.
    /// </summary>
    /// <param name="options">The store options that supply schema and table names.</param>
    /// <returns>The table references and options shared by schema operations.</returns>
    public static (PostgreSqlTableReference StoreTable, PostgreSqlTableReference MetadataTable) FromOptions(
        IPostgreSqlStoreTableOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return (PostgreSqlTableReference.ForStore(options), PostgreSqlTableReference.ForMetadata(options));
    }

    /// <summary>
    ///     Creates a schema operation context for one component definition and store table.
    /// </summary>
    /// <param name="connection">The open PostgreSQL connection.</param>
    /// <param name="options">The store table and metadata options.</param>
    /// <param name="definition">The component schema definition that supplies version metadata.</param>
    /// <param name="logger">The optional schema logger; when <see langword="null" />, operations run silently.</param>
    /// <returns>The schema operation context.</returns>
    public static PostgreSqlSchemaOperationContext ForComponent(
        NpgsqlConnection connection,
        IPostgreSqlStoreTableOptions options,
        PostgreSqlComponentSchemaDefinition definition,
        IPostgreSqlSchemaLogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var (storeTable, metadataTable) = FromOptions(options);

        return new PostgreSqlSchemaOperationContext(
            connection,
            options,
            definition.Component,
            storeTable,
            metadataTable,
            definition,
            logger ?? NullPostgreSqlSchemaLogger.Instance);
    }

    /// <summary>
    ///     Creates a schema operation context for version metadata reads and writes on one table.
    /// </summary>
    /// <param name="connection">The open PostgreSQL connection.</param>
    /// <param name="options">The store table and metadata options.</param>
    /// <param name="component">The LiteBus store component name.</param>
    /// <param name="table">The physical table whose version is read or written.</param>
    /// <param name="logger">The schema logger that receives operational output.</param>
    /// <returns>The schema operation context.</returns>
    public static PostgreSqlSchemaOperationContext ForVersionLookup(
        NpgsqlConnection connection,
        IPostgreSqlStoreTableOptions options,
        string component,
        PostgreSqlTableReference table,
        IPostgreSqlSchemaLogger logger)
    {
        return new PostgreSqlSchemaOperationContext(
            connection,
            options,
            component,
            table,
            PostgreSqlTableReference.ForMetadata(options),
            null,
            logger);
    }
}