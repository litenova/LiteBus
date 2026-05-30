namespace LiteBus.Outbox.Storage.EntityFrameworkCore;

/// <summary>
///     Defines Entity Framework Core outbox store options.
/// </summary>
public sealed record EfCoreOutboxStoreOptions
{
    /// <summary>
    ///     Gets the database schema name that stores outbox messages.
    /// </summary>
    public string SchemaName { get; init; } = "public";

    /// <summary>
    ///     Gets the table name that stores outbox messages.
    /// </summary>
    public string TableName { get; init; } = "litebus_outbox_messages";
}
