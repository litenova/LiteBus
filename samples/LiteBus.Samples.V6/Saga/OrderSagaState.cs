namespace LiteBus.Samples.V6.Saga;

/// <summary>
///     Mutable state for the order saga workflow demonstrated in the v6 sample.
/// </summary>
public sealed class OrderSagaState
{
    /// <summary>
    ///     Gets or sets the number of correlated commands processed for this order.
    /// </summary>
    public int Step { get; set; }
}