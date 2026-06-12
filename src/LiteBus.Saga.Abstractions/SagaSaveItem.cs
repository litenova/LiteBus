namespace LiteBus.Saga.Abstractions;

/// <summary>
///     Describes one saga state save with optimistic concurrency expectations.
/// </summary>
/// <typeparam name="TState">The saga state type being persisted.</typeparam>
/// <param name="Correlation">The correlation that identifies the saga instance.</param>
/// <param name="State">The state snapshot to persist.</param>
/// <param name="ExpectedVersion">
///     The version observed on the last load. Use <c>0</c> when inserting a new saga row.
/// </param>
public sealed record SagaSaveItem<TState>(SagaCorrelation Correlation, TState State, int ExpectedVersion)
    where TState : class, new();
