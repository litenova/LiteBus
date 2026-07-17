using System.Collections.Concurrent;
using LiteBus.Sample.Queries;

namespace LiteBus.Sample;

/// <summary>
///     Stores sample payment status in process so the query endpoint can observe command execution.
/// </summary>
public sealed class PaymentLedger
{
    /// <summary>
    ///     The payment status entries keyed by payment identifier.
    /// </summary>
    private readonly ConcurrentDictionary<Guid, PaymentStatus> _entries = new();

    /// <summary>
    ///     Records a processed payment.
    /// </summary>
    /// <param name="paymentId">The payment identifier.</param>
    /// <param name="amount">The processed amount.</param>
    public void MarkProcessed(Guid paymentId, decimal amount)
    {
        _entries[paymentId] = new PaymentStatus(paymentId, amount, "processed");
    }

    /// <summary>
    ///     Finds the current payment status.
    /// </summary>
    /// <param name="paymentId">The payment identifier.</param>
    /// <returns>The status when present; otherwise, <see langword="null" />.</returns>
    public PaymentStatus? Find(Guid paymentId)
    {
        return _entries.GetValueOrDefault(paymentId);
    }
}
