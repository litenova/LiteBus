namespace LiteBus.Outbox.Storage.PostgreSql;

/// <summary>
///     Defines the PostgreSQL notification channel used to wake outbox processors after inserts.
/// </summary>
public static class PostgreSqlOutboxNotifyChannel
{
    /// <summary>
    ///     Gets the <c>LISTEN</c> / <c>NOTIFY</c> channel name used by outbox insert triggers when schema supports them.
    /// </summary>
    public const string ChannelName = "litebus_outbox_work";
}