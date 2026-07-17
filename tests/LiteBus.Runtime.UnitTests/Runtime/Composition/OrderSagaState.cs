namespace LiteBus.Runtime.UnitTests.Runtime.Composition;

/// <summary>
///     Mutable saga state used by composition smoke tests.
/// </summary>
public sealed class OrderSagaState
{
    /// <summary>
    ///     Gets or sets the number of correlated commands processed for this order.
    /// </summary>
    public int Step { get; set; }
}
