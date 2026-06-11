using LiteBus.Storage.EntityFrameworkCore;

namespace LiteBus.Inbox.Storage.EntityFrameworkCore;

/// <summary>
///     Options for the Entity Framework Core inbox store and its table mapping.
/// </summary>
public sealed record EfCoreInboxStoreOptions
{
    /// <summary>
    ///     Gets the database schema that contains the inbox table.
    /// </summary>
    /// <value>The schema name. The default is <c>public</c> for PostgreSQL-oriented setups.</value>
    public string SchemaName { get; init; } = "public";

    /// <summary>
    ///     Gets the inbox table name.
    /// </summary>
    /// <value>The table name. The default is <c>litebus_inbox_messages</c>.</value>
    public string TableName { get; init; } = "litebus_inbox_messages";

    /// <summary>
    ///     Gets an optional lease provider override.
    /// </summary>
    /// <value>
    ///     When set, leasing uses the specified provider dialect instead of inferring it from the active
    ///     <see cref="Microsoft.EntityFrameworkCore.DbContext" />.
    /// </value>
    public EfCoreStorageProvider? LeaseProvider { get; init; }
}