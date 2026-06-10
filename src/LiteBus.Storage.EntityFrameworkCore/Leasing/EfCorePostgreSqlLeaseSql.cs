namespace LiteBus.Storage.EntityFrameworkCore.Leasing;

/// <summary>
///     Builds PostgreSQL lease SQL for inbox and outbox tables.
/// </summary>
internal static class EfCorePostgreSqlLeaseSql
{
    /// <summary>
    ///     Builds PostgreSQL lease SQL for one store table.
    /// </summary>
    /// <param name="component">The lease component.</param>
    /// <param name="qualifiedTableName">The quoted qualified table name.</param>
    /// <returns>The lease SQL.</returns>
    internal static string Build(EfCoreLeaseComponent component, string qualifiedTableName)
    {
        var idColumn = EfCoreLeaseTableMetadata.GetIdColumn(component);
        var alias = EfCoreLeaseTableMetadata.GetTableAlias(component);
        var returningProjection = component == EfCoreLeaseComponent.Inbox
            ? BuildInboxReturning(alias)
            : BuildOutboxReturning(alias);

        return """
               WITH candidates AS (
                   SELECT __ID_COLUMN__
                   FROM __TABLE__
                   WHERE
                       ({7}::text IS NULL OR tenant_id = {7}::text)
                       AND ((status IN ({0}::integer, {1}::integer) AND (visible_after IS NULL OR visible_after <= {2}::timestamptz))
                        OR (status = {3}::integer AND lease_expires_at IS NOT NULL AND lease_expires_at <= {2}::timestamptz)
                        OR (status = {3}::integer AND lease_expires_at IS NULL AND created_at < {8}::timestamptz))
                   ORDER BY created_at ASC
                   LIMIT {4}::integer
                   FOR UPDATE SKIP LOCKED
               )
               UPDATE __TABLE__ AS __ALIAS__
               SET
                   status = {3}::integer,
                   lease_owner = {5}::text,
                   lease_expires_at = {6}::timestamptz,
                   attempt_count = __ALIAS__.attempt_count + 1
               FROM candidates
               WHERE __ALIAS__.__ID_COLUMN__ = candidates.__ID_COLUMN__
               RETURNING
                   __RETURNING__;
               """
            .Replace("__TABLE__", qualifiedTableName, StringComparison.Ordinal)
            .Replace("__ID_COLUMN__", idColumn, StringComparison.Ordinal)
            .Replace("__ALIAS__", alias, StringComparison.Ordinal)
            .Replace("__RETURNING__", returningProjection, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Builds the inbox returning projection for PostgreSQL lease SQL.
    /// </summary>
    /// <param name="alias">The inbox table alias.</param>
    /// <returns>The returning column list.</returns>
    private static string BuildInboxReturning(string alias)
    {
        return $"""
                {alias}.message_id,
                {alias}.contract_name,
                {alias}.contract_version,
                {alias}.payload,
                {alias}.created_at,
                {alias}.visible_after,
                {alias}.attempt_count,
                {alias}.status,
                {alias}.idempotency_key,
                {alias}.lease_owner,
                {alias}.lease_expires_at,
                {alias}.last_error,
                {alias}.correlation_id,
                {alias}.causation_id,
                {alias}.tenant_id,
                {alias}.trace_context
                """;
    }

    /// <summary>
    ///     Builds the outbox returning projection for PostgreSQL lease SQL.
    /// </summary>
    /// <param name="alias">The outbox table alias.</param>
    /// <returns>The returning column list.</returns>
    private static string BuildOutboxReturning(string alias)
    {
        return $"""
                {alias}.message_id,
                {alias}.contract_name,
                {alias}.contract_version,
                {alias}.payload,
                {alias}.topic,
                {alias}.created_at,
                {alias}.visible_after,
                {alias}.status,
                {alias}.attempt_count,
                {alias}.lease_owner,
                {alias}.lease_expires_at,
                {alias}.last_error,
                {alias}.correlation_id,
                {alias}.causation_id,
                {alias}.tenant_id,
                {alias}.trace_context
                """;
    }
}
