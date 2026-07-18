using LiteBus.Messaging.Abstractions.DurableMessaging;

namespace LiteBus.Storage.PostgreSql.Stores;

/// <summary>
///     Shared PostgreSQL idempotency lookup SQL for durable inbox and outbox stores.
/// </summary>
internal static class PostgreSqlIdempotencyResolution
{
    /// <summary>
    ///     Builds SQL that reads the row skipped by an idempotent insert.
    /// </summary>
    /// <param name="tableName">The qualified table name.</param>
    /// <param name="selectColumnsSql">The SELECT column list shared by the store reader.</param>
    /// <param name="idempotencyKey">The idempotency key from the attempted insert.</param>
    /// <returns>The lookup SQL and whether a tenant parameter is required.</returns>
    internal static (string Sql, bool RequiresTenantParameter) BuildFindExistingSql(
        string tableName,
        string selectColumnsSql,
        string? idempotencyKey)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return ($"""
                    SELECT {selectColumnsSql}
                    FROM {tableName}
                    WHERE message_id = @message_id
                    LIMIT 1;
                    """, false);
        }

        return ($"""
                SELECT {selectColumnsSql}
                FROM {tableName}
                WHERE message_id = @message_id
                   OR (idempotency_key = @idempotency_key AND tenant_id = @tenant_id)
                ORDER BY CASE WHEN message_id = @message_id THEN 0 ELSE 1 END
                LIMIT 1;
                """, true);
    }

    /// <summary>
    ///     Normalizes tenant identifiers before they are written to PostgreSQL parameters.
    /// </summary>
    /// <param name="tenantId">The tenant identifier supplied by the caller.</param>
    /// <returns>The normalized tenant identifier.</returns>
    internal static string NormalizeTenantParameter(string? tenantId)
    {
        return DurableTenantId.Normalize(tenantId);
    }

    /// <summary>
    ///     Creates the composite scope key used by batch idempotency resolution.
    /// </summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="idempotencyKey">The idempotency key.</param>
    /// <returns>The composite scope key.</returns>
    internal static string CreateScopeKey(string? tenantId, string idempotencyKey)
    {
        return DurableIdempotencyScope.CreateScopeKey(tenantId, idempotencyKey);
    }
}
