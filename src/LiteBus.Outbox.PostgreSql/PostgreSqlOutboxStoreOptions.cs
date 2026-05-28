namespace LiteBus.Outbox.PostgreSql;

/// <summary>
///     Defines PostgreSQL outbox store options.
/// </summary>
public sealed record PostgreSqlOutboxStoreOptions
{
    /// <summary>
    ///     Gets the PostgreSQL schema name.
    /// </summary>
    public string SchemaName { get; init; } = "public";

    /// <summary>
    ///     Gets the PostgreSQL table name.
    /// </summary>
    public string TableName { get; init; } = "litebus_outbox_messages";
}