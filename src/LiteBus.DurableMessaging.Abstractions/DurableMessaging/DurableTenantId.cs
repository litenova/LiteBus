namespace LiteBus.Messaging.Abstractions.DurableMessaging;

/// <summary>
///     Normalizes tenant identifiers for durable store idempotency scopes and persisted rows.
/// </summary>
public static class DurableTenantId
{
    /// <summary>
    ///     Normalizes nullable tenant identifiers to the empty string used for unscoped rows.
    /// </summary>
    /// <param name="tenantId">The tenant identifier supplied by the caller.</param>
    /// <returns>The normalized tenant identifier stored in durable rows and idempotency indexes.</returns>
    public static string Normalize(string? tenantId)
    {
        return string.IsNullOrWhiteSpace(tenantId) ? string.Empty : tenantId;
    }
}
