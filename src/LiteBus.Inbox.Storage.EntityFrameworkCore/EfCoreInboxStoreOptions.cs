namespace LiteBus.Inbox.Storage.EntityFrameworkCore;

/// <summary>
///     Options for the Entity Framework Core inbox store and its table mapping.
/// </summary>
public sealed class EfCoreInboxStoreOptions
{
    /// <summary>
    ///     Gets or sets the database schema that contains the inbox table.
    /// </summary>
    /// <value>The schema name. The default is <c>public</c>.</value>
    public string SchemaName { get; set; } = "public";

    /// <summary>
    ///     Gets or sets the inbox table name.
    /// </summary>
    /// <value>The table name. The default is <c>litebus_inbox_commands</c>.</value>
    public string TableName { get; set; } = "litebus_inbox_commands";
}
