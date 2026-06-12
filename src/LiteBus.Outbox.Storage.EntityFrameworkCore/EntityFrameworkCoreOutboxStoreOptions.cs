using LiteBus.Storage.EntityFrameworkCore;

namespace LiteBus.Outbox.Storage.EntityFrameworkCore;

/// <summary>
///     Defines Entity Framework Core outbox store options.
/// </summary>
public sealed record EntityFrameworkCoreOutboxStoreOptions
{
    /// <summary>
    ///     Gets the database schema name that stores outbox messages.
    /// </summary>
    public string SchemaName { get; init; } = "public";

    /// <summary>
    ///     Gets the table name that stores outbox messages.
    /// </summary>
    public string TableName { get; init; } = "litebus_outbox_messages";

    /// <summary>
    ///     Gets an optional lease provider override.
    /// </summary>
    /// <value>
    ///     When set, leasing uses the specified provider dialect instead of inferring it from the active
    ///     <see cref="Microsoft.EntityFrameworkCore.DbContext" />.
    /// </value>
    public EfCoreStorageProvider? LeaseProvider { get; init; }
}