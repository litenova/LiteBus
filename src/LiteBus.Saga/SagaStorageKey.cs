namespace LiteBus.Saga;

/// <summary>
///     Identifies one saga row without delimiter-based composite key collisions.
/// </summary>
internal readonly record struct SagaStorageKey
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="SagaStorageKey" /> structure.
    /// </summary>
    /// <param name="tenantId">The normalized tenant identifier.</param>
    /// <param name="sagaDefinitionId">The saga definition identifier.</param>
    /// <param name="correlationId">The correlation identifier.</param>
    internal SagaStorageKey(string tenantId, string sagaDefinitionId, string correlationId)
    {
        TenantId = tenantId;
        SagaDefinitionId = sagaDefinitionId;
        CorrelationId = correlationId;
    }

    /// <summary>
    ///     Gets the normalized tenant identifier.
    /// </summary>
    internal string TenantId { get; }

    /// <summary>
    ///     Gets the saga definition identifier.
    /// </summary>
    internal string SagaDefinitionId { get; }

    /// <summary>
    ///     Gets the correlation identifier.
    /// </summary>
    internal string CorrelationId { get; }
}
