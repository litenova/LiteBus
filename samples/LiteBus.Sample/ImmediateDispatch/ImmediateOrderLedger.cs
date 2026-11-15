using System.Collections.Concurrent;

namespace LiteBus.Sample.ImmediateDispatch;

/// <summary>
///     Stores immediate order status in process so the immediate order endpoint can observe committed orders.
/// </summary>
public sealed class ImmediateOrderLedger
{
    /// <summary>
    ///     The order status entries keyed by order identifier.
    /// </summary>
    private readonly ConcurrentDictionary<Guid, ImmediateOrderStatus> _entries = new();

    /// <summary>
    ///     Records a placed immediate order.
    /// </summary>
    /// <param name="orderId">The order identifier.</param>
    /// <param name="amount">The placed amount.</param>
    public void MarkPlaced(Guid orderId, decimal amount)
    {
        _entries[orderId] = new ImmediateOrderStatus(orderId, amount, "placed");
    }

    /// <summary>
    ///     Finds the current immediate order status.
    /// </summary>
    /// <param name="orderId">The order identifier.</param>
    /// <returns>The status when present; otherwise, <see langword="null" />.</returns>
    public ImmediateOrderStatus? Find(Guid orderId)
    {
        return _entries.GetValueOrDefault(orderId);
    }
}
