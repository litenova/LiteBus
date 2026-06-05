namespace LiteBus.Saga.Storage.PostgreSql;

/// <summary>
///     Options for the PostgreSQL saga store table and schema bootstrap.
/// </summary>
public sealed class PostgreSqlSagaStoreOptions
{
    /// <summary>
    ///     Gets the PostgreSQL schema that contains saga tables.
    /// </summary>
    public string SchemaName { get; init; } = "public";

    /// <summary>
    ///     Gets the saga instances table name.
    /// </summary>
    public string TableName { get; init; } = "litebus_saga_instances";

    /// <summary>
    ///     Gets a value indicating whether schema creation runs during host startup.
    /// </summary>
    public bool EnsureSchemaCreationOnStartup { get; init; }

    /// <summary>
    ///     Gets a value indicating whether schema validation runs during host startup.
    /// </summary>
    public bool ValidateSchemaCreationOnStartup { get; init; }
}
