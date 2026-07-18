namespace LiteBus.Inbox.Storage.PostgreSql;

/// <summary>
///     Defines the PostgreSQL notification channel used to wake inbox processors after inserts.
/// </summary>
public static class PostgreSqlInboxNotifyChannel
{
    /// <summary>
    ///     Gets the <c>LISTEN</c> / <c>NOTIFY</c> channel name used by inbox insert triggers.
    /// </summary>
    public const string ChannelName = "litebus_inbox_work";
}