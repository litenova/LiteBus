namespace LiteBus.Saga.Abstractions;

/// <summary>
///     Identifies one saga instance within durable storage.
/// </summary>
/// <remarks>
///     <para>
///         <see cref="CorrelationId" /> typically matches the inbox envelope correlation identifier so related messages
///         converge on one saga row. <see cref="SagaDefinitionId" /> is the stable workflow partition and may differ
///         from individual message contract names when several contracts advance the same saga.
///     </para>
///     <para>
///         <see cref="TenantId" /> scopes the row when multi-tenant hosts share one saga table. Use <see langword="null" />
///         or omit for single-tenant deployments.
///     </para>
/// </remarks>
public sealed record SagaCorrelation
{
    /// <summary>
    ///     Gets the correlation identifier that groups related saga messages.
    /// </summary>
    public required string CorrelationId { get; init; }

    /// <summary>
    ///     Gets the saga definition identifier used to resolve state shape and storage partition.
    /// </summary>
    public required string SagaDefinitionId { get; init; }

    /// <summary>
    ///     Gets the optional tenant identifier included in the storage primary key.
    /// </summary>
    public string? TenantId { get; init; }
}
