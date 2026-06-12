using LiteBus.Messaging.Abstractions.DurableMessaging;

namespace LiteBus.Storage.EntityFrameworkCore.Stores;

/// <summary>
///     Shared idempotency resolution helpers for Entity Framework Core durable stores.
/// </summary>
internal static class EfCoreIdempotencyResolution
{
    /// <summary>
    ///     Normalizes tenant identifiers before they are compared or indexed.
    /// </summary>
    /// <param name="tenantId">The tenant identifier supplied by the caller.</param>
    /// <returns>The normalized tenant identifier.</returns>
    internal static string NormalizeTenantId(string? tenantId)
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

    /// <summary>
    ///     Records one batch result so later duplicate identifiers resolve to the same envelope.
    /// </summary>
    /// <typeparam name="TEnvelope">The envelope type returned to callers.</typeparam>
    /// <param name="seenIds">The message identifiers already resolved in the batch.</param>
    /// <param name="seenIdempotencyScopes">The tenant-scoped idempotency keys already resolved in the batch.</param>
    /// <param name="envelope">The source envelope from the batch request.</param>
    /// <param name="result">The resolved envelope returned for the batch slot.</param>
    /// <param name="readMessageId">Reads the message identifier from one envelope.</param>
    /// <param name="readTenantId">Reads the tenant identifier from one envelope.</param>
    /// <param name="readIdempotencyKey">Reads the idempotency key from one envelope.</param>
    internal static void RememberBatchResult<TEnvelope>(
        Dictionary<Guid, TEnvelope> seenIds,
        Dictionary<string, TEnvelope> seenIdempotencyScopes,
        TEnvelope envelope,
        TEnvelope result,
        Func<TEnvelope, Guid> readMessageId,
        Func<TEnvelope, string?> readTenantId,
        Func<TEnvelope, string?> readIdempotencyKey)
    {
        seenIds[readMessageId(envelope)] = result;

        var idempotencyKey = readIdempotencyKey(envelope);

        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            seenIdempotencyScopes[CreateScopeKey(readTenantId(envelope), idempotencyKey)] = result;
        }
    }

    /// <summary>
    ///     Tries to resolve a duplicate envelope from the in-batch idempotency scope cache.
    /// </summary>
    /// <typeparam name="TEnvelope">The envelope type returned to callers.</typeparam>
    /// <param name="seenIdempotencyScopes">The tenant-scoped idempotency keys already resolved in the batch.</param>
    /// <param name="envelope">The source envelope from the batch request.</param>
    /// <param name="readTenantId">Reads the tenant identifier from one envelope.</param>
    /// <param name="readIdempotencyKey">Reads the idempotency key from one envelope.</param>
    /// <param name="duplicate">The duplicate envelope resolved from the batch cache.</param>
    /// <returns><see langword="true" /> when a duplicate was found in the batch cache.</returns>
    internal static bool TryGetBatchDuplicate<TEnvelope>(
        Dictionary<string, TEnvelope> seenIdempotencyScopes,
        TEnvelope envelope,
        Func<TEnvelope, string?> readTenantId,
        Func<TEnvelope, string?> readIdempotencyKey,
        out TEnvelope duplicate)
    {
        var idempotencyKey = readIdempotencyKey(envelope);

        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            duplicate = default!;
            return false;
        }

        return seenIdempotencyScopes.TryGetValue(
            CreateScopeKey(readTenantId(envelope), idempotencyKey),
            out duplicate!);
    }
}
