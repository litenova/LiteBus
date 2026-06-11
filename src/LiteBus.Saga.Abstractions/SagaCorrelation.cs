namespace LiteBus.Saga.Abstractions;

/// <summary>
///     Identifies one saga instance within durable storage.
/// </summary>
/// <remarks>
///     <para>
///         <see cref="CorrelationId" /> typically matches the inbox envelope correlation identifier so related messages
///         converge on one saga row. <see cref="SagaType" /> distinguishes state shape when multiple sagas share a
///         correlation namespace.
///     </para>
/// </remarks>
public sealed record SagaCorrelation
{
    /// <summary>
    ///     Gets the correlation identifier that groups related saga messages.
    /// </summary>
    public required string CorrelationId { get; init; }

    /// <summary>
    ///     Gets the saga type name used to resolve state shape and storage partition.
    /// </summary>
    public required string SagaType { get; init; }
}