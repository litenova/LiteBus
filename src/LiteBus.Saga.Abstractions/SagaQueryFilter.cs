namespace LiteBus.Saga.Abstractions;

/// <summary>
///     Optional predicates for querying persisted saga instances.
/// </summary>
public sealed record SagaQueryFilter
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
    ///     Gets a value indicating whether only completed instances are returned.
    /// </summary>
    public bool? IsCompleted { get; init; }

    /// <summary>
    ///     Gets the maximum number of rows to return.
    /// </summary>
    public int Take { get; init; } = 100;
}
