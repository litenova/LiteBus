namespace LiteBus.Saga.Abstractions;

/// <summary>
///     Represents one loaded saga instance and its optimistic concurrency token.
/// </summary>
/// <typeparam name="TState">The saga state type.</typeparam>
public sealed class SagaInstance<TState>
    where TState : class, new()
{
    /// <summary>
    ///     Gets the correlation that identifies this saga instance.
    /// </summary>
    public required SagaCorrelation Correlation { get; init; }

    /// <summary>
    ///     Gets the current saga state deserialized from storage.
    /// </summary>
    public required TState State { get; init; }

    /// <summary>
    ///     Gets the optimistic lock version used on the next save attempt.
    /// </summary>
    public required int Version { get; init; }

    /// <summary>
    ///     Gets a value indicating whether the saga has already been completed in storage.
    /// </summary>
    public required bool IsCompleted { get; init; }
}
