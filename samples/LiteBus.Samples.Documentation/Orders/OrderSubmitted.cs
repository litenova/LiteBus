namespace LiteBus.Samples.Documentation.Orders;

/// <summary>
///     Announces that an order was placed and stored in the outbox.
/// </summary>
public sealed record OrderSubmitted
{
    /// <summary>
    ///     Gets the identifier of the order that was placed.
    /// </summary>
    public Guid OrderId { get; init; }
}
