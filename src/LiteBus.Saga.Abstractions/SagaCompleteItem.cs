namespace LiteBus.Saga.Abstractions;

/// <summary>
///     Describes one saga completion with optimistic concurrency expectations.
/// </summary>
/// <param name="Correlation">The correlation that identifies the saga instance.</param>
/// <param name="ExpectedVersion">
///     The version observed on the last load. Completion fails when the stored version does not match.
/// </param>
public sealed record SagaCompleteItem(SagaCorrelation Correlation, int ExpectedVersion)
{
    /// <summary>
    ///     Creates a completion item from the active correlation and expected version.
    /// </summary>
    /// <param name="correlation">The saga correlation.</param>
    /// <param name="expectedVersion">The optimistic lock version observed on load.</param>
    /// <returns>The completion item passed to <see cref="ISagaStore.CompleteAsync" />.</returns>
    public static SagaCompleteItem From(SagaCorrelation correlation, int expectedVersion)
    {
        ArgumentNullException.ThrowIfNull(correlation);

        return new SagaCompleteItem(correlation, expectedVersion);
    }
}
