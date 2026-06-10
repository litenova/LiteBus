namespace LiteBus.Storage.EntityFrameworkCore.Leasing;

/// <summary>
///     Builds MySQL lease SQL statements executed inside one transaction.
/// </summary>
internal static class EfCoreMySqlLeaseSql
{
    /// <summary>
    ///     Placeholder replaced with a parameterized IN clause at runtime.
    /// </summary>
    internal const string InClauseToken = "__IN_CLAUSE__";

    /// <summary>
    ///     Builds the candidate selection SQL for a MySQL lease operation.
    /// </summary>
    /// <param name="component">The lease component.</param>
    /// <param name="qualifiedTableName">The backtick-quoted qualified table name.</param>
    /// <returns>The select SQL.</returns>
    internal static string BuildSelectCandidates(EfCoreLeaseComponent component, string qualifiedTableName)
    {
        var idColumn = EfCoreLeaseTableMetadata.GetIdColumn(component);

        return """
               SELECT `__ID_COLUMN__` AS `Value`
               FROM __TABLE__
               WHERE
                   ({5} IS NULL OR `tenant_id` = {5})
                   AND ((`status` IN ({0}, {1}) AND (`visible_after` IS NULL OR `visible_after` <= {2}))
                    OR (`status` = {3} AND `lease_expires_at` IS NOT NULL AND `lease_expires_at` <= {2})
                    OR (`status` = {3} AND `lease_expires_at` IS NULL AND `created_at` < {6}))
               ORDER BY `created_at` ASC
               LIMIT {4}
               FOR UPDATE SKIP LOCKED
               """
            .Replace("__TABLE__", qualifiedTableName, StringComparison.Ordinal)
            .Replace("__ID_COLUMN__", idColumn, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Builds the update SQL for a MySQL lease operation.
    /// </summary>
    /// <param name="component">The lease component.</param>
    /// <param name="qualifiedTableName">The backtick-quoted qualified table name.</param>
    /// <returns>The update SQL with an IN clause placeholder for identifiers.</returns>
    internal static string BuildUpdate(EfCoreLeaseComponent component, string qualifiedTableName)
    {
        var idColumn = EfCoreLeaseTableMetadata.GetIdColumn(component);
        var alias = EfCoreLeaseTableMetadata.GetTableAlias(component);

        return """
               UPDATE __TABLE__ AS `__ALIAS__`
               INNER JOIN (
                   SELECT `__ID_COLUMN__`
                   FROM __TABLE__
                   WHERE `__ID_COLUMN__` IN (__IN_CLAUSE__)
               ) AS candidates ON `__ALIAS__`.`__ID_COLUMN__` = candidates.`__ID_COLUMN__`
               SET
                   `__ALIAS__`.`status` = {3},
                   `__ALIAS__`.`lease_owner` = {5},
                   `__ALIAS__`.`lease_expires_at` = {6},
                   `__ALIAS__`.`attempt_count` = `__ALIAS__`.`attempt_count` + 1
               """
            .Replace("__TABLE__", qualifiedTableName, StringComparison.Ordinal)
            .Replace("__ID_COLUMN__", idColumn, StringComparison.Ordinal)
            .Replace("__ALIAS__", alias, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Builds the row reload SQL after a MySQL lease update.
    /// </summary>
    /// <param name="component">The lease component.</param>
    /// <param name="qualifiedTableName">The backtick-quoted qualified table name.</param>
    /// <returns>The select SQL with an IN clause placeholder for identifiers.</returns>
    internal static string BuildReload(EfCoreLeaseComponent component, string qualifiedTableName)
    {
        var idColumn = EfCoreLeaseTableMetadata.GetIdColumn(component);
        var selectList = component == EfCoreLeaseComponent.Inbox
            ? "`__ID_COLUMN__`, `contract_name`, `contract_version`, `payload`, `created_at`, `visible_after`, `attempt_count`, `status`, `idempotency_key`, `lease_owner`, `lease_expires_at`, `last_error`, `correlation_id`, `causation_id`, `tenant_id`, `trace_context`"
            : "`__ID_COLUMN__`, `contract_name`, `contract_version`, `payload`, `topic`, `created_at`, `visible_after`, `attempt_count`, `status`, `lease_owner`, `lease_expires_at`, `last_error`, `correlation_id`, `causation_id`, `tenant_id`, `trace_context`";

        return """
               SELECT __SELECT_LIST__
               FROM __TABLE__
               WHERE `__ID_COLUMN__` IN (__IN_CLAUSE__)
               ORDER BY `created_at` ASC
               """
            .Replace("__TABLE__", qualifiedTableName, StringComparison.Ordinal)
            .Replace("__ID_COLUMN__", idColumn, StringComparison.Ordinal)
            .Replace("__SELECT_LIST__", selectList, StringComparison.Ordinal);
    }
}
