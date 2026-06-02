namespace LiteBus.Storage.EntityFrameworkCore.Leasing;

/// <summary>
///     Supplies inbox and outbox column metadata used by raw lease SQL builders.
/// </summary>
internal static class EfCoreLeaseTableMetadata
{
    /// <summary>
    ///     Gets the primary key column name for one lease component.
    /// </summary>
    /// <param name="component">The lease component.</param>
    /// <returns>The primary key column name.</returns>
    internal static string GetIdColumn(EfCoreLeaseComponent component)
    {
        return component == EfCoreLeaseComponent.Inbox ? "command_id" : "message_id";
    }

    /// <summary>
    ///     Gets the table alias used in multi-statement lease SQL.
    /// </summary>
    /// <param name="component">The lease component.</param>
    /// <returns>The table alias.</returns>
    internal static string GetTableAlias(EfCoreLeaseComponent component)
    {
        return component == EfCoreLeaseComponent.Inbox ? "inbox" : "outbox";
    }
}
