namespace LiteBus.Inbox.Storage.EntityFrameworkCore;

/// <summary>
///     Options for the Entity Framework Core inbox store and its table mapping.
/// </summary>
public sealed class EfCoreInboxStoreOptions
{
    /// <summary>
    ///     Gets or sets the database schema that contains the inbox table.
    /// </summary>
    /// <value>The schema name. The default is <c>litebus</c>.</value>
    public string SchemaName { get; set; } = "litebus";

    /// <summary>
    ///     Gets or sets the inbox table name.
    /// </summary>
    /// <value>The table name. The default is <c>inbox_messages</c>.</value>
    public string TableName { get; set; } = "inbox_messages";
}
