namespace LiteBus.Saga.Abstractions;

/// <summary>
///     Persists saga state keyed by <see cref="SagaCorrelation" /> with optimistic concurrency control.
/// </summary>
public interface ISagaStore
{
    /// <summary>
    ///     Loads saga state for the supplied correlation when a row exists.
    /// </summary>
    /// <typeparam name="TState">The saga state type.</typeparam>
    /// <param name="correlation">The correlation that identifies the saga instance.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>
    ///     The loaded saga instance, or <see langword="null" /> when no row exists and a new saga should start with
    ///     default state.
    /// </returns>
    Task<SagaInstance<TState>?> LoadAsync<TState>(
        SagaCorrelation correlation,
        CancellationToken cancellationToken = default)
        where TState : class, new();

    /// <summary>
    ///     Saves saga state using optimistic concurrency on the supplied expected version.
    /// </summary>
    /// <typeparam name="TState">The saga state type.</typeparam>
    /// <param name="item">The correlation, state snapshot, and expected version for the save.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>A task that completes when the save succeeds.</returns>
    /// <exception cref="SagaConcurrencyException">
    ///     Thrown when another worker updated the saga row before this save completed.
    /// </exception>
    Task SaveAsync<TState>(
        SagaSaveItem<TState> item,
        CancellationToken cancellationToken = default)
        where TState : class, new();

    /// <summary>
    ///     Marks a saga instance completed so subsequent loads return <see cref="SagaInstance{TState}.IsCompleted" />
    ///     <see langword="true" />.
    /// </summary>
    /// <param name="item">The correlation and expected version for completion.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>A task that completes when completion is recorded.</returns>
    /// <exception cref="SagaConcurrencyException">
    ///     Thrown when the stored version does not match <see cref="SagaCompleteItem.ExpectedVersion" />.
    /// </exception>
    Task CompleteAsync(SagaCompleteItem item, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Queries persisted saga rows that match the supplied filter.
    /// </summary>
    /// <param name="filter">The optional query predicates.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The matching saga summaries.</returns>
    Task<IReadOnlyList<SagaInstanceSummary>> QueryAsync(
        SagaQueryFilter filter,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Purges persisted saga rows that match the supplied filter.
    /// </summary>
    /// <param name="filter">The optional purge predicates.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The number of rows removed.</returns>
    Task<int> PurgeAsync(SagaPurgeFilter filter, CancellationToken cancellationToken = default);
}
