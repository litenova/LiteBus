namespace LiteBus.Storage.PostgreSql;

/// <summary>
///     Identifies LiteBus PostgreSQL store components tracked in the schema version metadata table.
/// </summary>
public static class PostgreSqlSchemaComponents
{
    /// <summary>
    ///     The inbox store component name.
    /// </summary>
    public const string Inbox = "inbox";

    /// <summary>
    ///     The outbox store component name.
    /// </summary>
    public const string Outbox = "outbox";

    /// <summary>
    ///     The saga store component name.
    /// </summary>
    public const string Saga = "saga";
}