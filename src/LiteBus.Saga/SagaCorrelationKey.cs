using LiteBus.Saga.Abstractions;

namespace LiteBus.Saga;

/// <summary>
///     Normalizes <see cref="SagaCorrelation" /> values for storage keys and SQL parameters.
/// </summary>
internal static class SagaCorrelationKey
{
    /// <summary>
    ///     Builds the in-memory storage key for one saga correlation.
    /// </summary>
    /// <param name="correlation">The saga correlation.</param>
    /// <returns>The composite storage key.</returns>
    internal static string BuildStorageKey(SagaCorrelation correlation)
    {
        ArgumentNullException.ThrowIfNull(correlation);

        return $"{NormalizeTenantId(correlation.TenantId)}:{correlation.SagaDefinitionId}:{correlation.CorrelationId}";
    }

    /// <summary>
    ///     Normalizes tenant identifiers for primary-key storage.
    /// </summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <returns>The normalized tenant identifier stored in saga rows.</returns>
    internal static string NormalizeTenantId(string? tenantId)
    {
        return string.IsNullOrWhiteSpace(tenantId) ? string.Empty : tenantId;
    }
}
