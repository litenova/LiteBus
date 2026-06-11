namespace LiteBus.Storage.EntityFrameworkCore.Leasing;

/// <summary>
///     Supplies inbox and outbox column metadata used by raw lease SQL builders.
/// </summary>
internal static class EfCoreLeaseTableMetadata
{
    /// <summary>
    ///     Gets the primary key column name for inbox and outbox lease SQL.
    /// </summary>
    /// <param name="component">The lease component (inbox and outbox use the same column name).</param>
    /// <returns>The primary key column name <c>message_id</c>.</returns>
    internal static string GetIdColumn(EfCoreLeaseComponent component)
    {
        _ = component;
        return "message_id";
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