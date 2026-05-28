namespace LiteBus.Inbox.PostgreSql;

/// <summary>
///     Defines PostgreSQL command inbox store options.
/// </summary>
public sealed record PostgreSqlInboxStoreOptions
{
    /// <summary>
    ///     Gets the PostgreSQL schema name.
    /// </summary>
    public string SchemaName { get; init; } = "public";

    /// <summary>
    ///     Gets the PostgreSQL table name.
    /// </summary>
    public string TableName { get; init; } = "litebus_inbox_commands";
}