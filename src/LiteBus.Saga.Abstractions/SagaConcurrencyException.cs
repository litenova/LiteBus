namespace LiteBus.Saga.Abstractions;

/// <summary>
///     Thrown when optimistic saga persistence detects a concurrent update.
/// </summary>
public sealed class SagaConcurrencyException : Exception
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="SagaConcurrencyException" /> class.
    /// </summary>
    /// <param name="correlation">The correlation whose save failed due to a version conflict.</param>
    public SagaConcurrencyException(SagaCorrelation correlation)
        : base($"Saga '{correlation.SagaType}' with correlation '{correlation.CorrelationId}' was updated concurrently.")
    {
        Correlation = correlation;
    }

    /// <summary>
    ///     Gets the correlation whose save failed due to a version conflict.
    /// </summary>
    public SagaCorrelation Correlation { get; }
}