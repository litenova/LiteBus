using System;

namespace LiteBus.Messaging.Abstractions.DurableMessaging;

/// <summary>
///     Builds composite idempotency scope keys for in-memory indexes and batch deduplication.
/// </summary>
public static class DurableIdempotencyScope
{
    /// <summary>
    ///     The separator between normalized tenant and idempotency key segments.
    /// </summary>
    private const char ScopeSeparator = '\u001f';

    /// <summary>
    ///     Creates a composite scope key from a tenant identifier and idempotency key.
    /// </summary>
    /// <param name="tenantId">The optional tenant identifier.</param>
    /// <param name="idempotencyKey">The idempotency key.</param>
    /// <returns>The composite scope key.</returns>
    public static string CreateScopeKey(string? tenantId, string idempotencyKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);

        return string.Concat(DurableTenantId.Normalize(tenantId), ScopeSeparator, idempotencyKey);
    }
}
