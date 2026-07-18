namespace LiteBus.Saga.Abstractions;

/// <summary>
///     Optional predicates for purging persisted saga instances.
/// </summary>
public sealed record SagaPurgeFilter
{
    /// <summary>
    ///     Gets the saga definition identifier filter.
    /// </summary>
    public string? SagaDefinitionId { get; init; }

    /// <summary>
    ///     Gets the correlation identifier filter.
    /// </summary>
    public string? CorrelationId { get; init; }

    /// <summary>
    ///     Gets the tenant identifier filter.
    /// </summary>
    public string? TenantId { get; init; }

    /// <summary>
    ///     Gets a value indicating whether only completed instances are purged.
    /// </summary>
    public bool? IsCompleted { get; init; }

    /// <summary>
    ///     Gets the UTC timestamp before which completed rows may be purged.
    /// </summary>
    public DateTimeOffset? CompletedBefore { get; init; }
}
