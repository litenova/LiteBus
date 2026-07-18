namespace LiteBus.Saga.Abstractions;

/// <summary>
///     Lightweight saga row returned from <see cref="ISagaStore.QueryAsync" />.
/// </summary>
public sealed record SagaInstanceSummary
{
    /// <summary>
    ///     Gets the correlation that identifies the saga instance.
    /// </summary>
    public required SagaCorrelation Correlation { get; init; }

    /// <summary>
    ///     Gets the optimistic lock version stored for the row.
    /// </summary>
    public required int Version { get; init; }

    /// <summary>
    ///     Gets a value indicating whether the saga instance is completed.
    /// </summary>
    public required bool IsCompleted { get; init; }

    /// <summary>
    ///     Gets the UTC timestamp when the row was created.
    /// </summary>
    public DateTimeOffset? CreatedAt { get; init; }

    /// <summary>
    ///     Gets the UTC timestamp when the row was last updated.
    /// </summary>
    public DateTimeOffset? UpdatedAt { get; init; }
}
