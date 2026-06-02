namespace LiteBus.Storage.EntityFrameworkCore.Leasing;

/// <summary>
///     Builds SQL Server lease SQL for inbox and outbox tables.
/// </summary>
internal static class EfCoreSqlServerLeaseSql
{
    /// <summary>
    ///     Builds SQL Server lease SQL for one store table.
    /// </summary>
    /// <param name="component">The lease component.</param>
    /// <param name="qualifiedTableName">The bracket-quoted qualified table name.</param>
    /// <returns>The lease SQL.</returns>
    internal static string Build(EfCoreLeaseComponent component, string qualifiedTableName)
    {
        var idColumn = EfCoreLeaseTableMetadata.GetIdColumn(component);
        var alias = EfCoreLeaseTableMetadata.GetTableAlias(component);
        var outputProjection = component == EfCoreLeaseComponent.Inbox
            ? BuildInboxOutput()
            : BuildOutboxOutput();

        return """
               ;WITH candidates AS (
                   SELECT TOP ({4}) [__ID_COLUMN__]
                   FROM __TABLE__ WITH (UPDLOCK, READPAST, ROWLOCK)
                   WHERE
                       (([status] IN ({0}, {1}) AND ([visible_after] IS NULL OR [visible_after] <= {2}))
                        OR ([status] = {3} AND [lease_expires_at] IS NOT NULL AND [lease_expires_at] <= {2}))
                   ORDER BY [created_at] ASC
               )
               UPDATE [__ALIAS__]
               SET
                   [status] = {3},
                   [lease_owner] = {5},
                   [lease_expires_at] = {6},
                   [attempt_count] = [__ALIAS__].[attempt_count] + 1
               OUTPUT
                   __OUTPUT__
               FROM __TABLE__ AS [__ALIAS__]
               INNER JOIN candidates ON [__ALIAS__].[__ID_COLUMN__] = candidates.[__ID_COLUMN__];
               """
            .Replace("__TABLE__", qualifiedTableName, StringComparison.Ordinal)
            .Replace("__ID_COLUMN__", idColumn, StringComparison.Ordinal)
            .Replace("__ALIAS__", alias, StringComparison.Ordinal)
            .Replace("__OUTPUT__", outputProjection, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Builds the inbox output projection for SQL Server lease SQL.
    /// </summary>
    /// <returns>The output column list.</returns>
    private static string BuildInboxOutput()
    {
        return """
               INSERTED.[message_id],
               INSERTED.[contract_name],
               INSERTED.[contract_version],
               INSERTED.[payload],
               INSERTED.[created_at],
               INSERTED.[visible_after],
               INSERTED.[attempt_count],
               INSERTED.[status],
               INSERTED.[idempotency_key],
               INSERTED.[lease_owner],
               INSERTED.[lease_expires_at],
               INSERTED.[last_error],
               INSERTED.[correlation_id],
               INSERTED.[causation_id],
               INSERTED.[tenant_id]
               """;
    }

    /// <summary>
    ///     Builds the outbox output projection for SQL Server lease SQL.
    /// </summary>
    /// <returns>The output column list.</returns>
    private static string BuildOutboxOutput()
    {
        return """
               INSERTED.[message_id],
               INSERTED.[contract_name],
               INSERTED.[contract_version],
               INSERTED.[payload],
               INSERTED.[topic],
               INSERTED.[created_at],
               INSERTED.[visible_after],
               INSERTED.[status],
               INSERTED.[attempt_count],
               INSERTED.[lease_owner],
               INSERTED.[lease_expires_at],
               INSERTED.[last_error],
               INSERTED.[correlation_id],
               INSERTED.[causation_id],
               INSERTED.[tenant_id]
               """;
    }
}
