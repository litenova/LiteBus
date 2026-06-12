namespace LiteBus.Samples.V6.Controllers;

/// <summary>
///     HTTP request body for accepting a payment into the inbox.
/// </summary>
public sealed class AcceptPaymentRequest
{
    /// <summary>
    ///     Gets or sets the payment identifier supplied by the caller.
    /// </summary>
    public Guid PaymentId { get; set; }

    /// <summary>
    ///     Gets or sets the payment amount.
    /// </summary>
    public decimal Amount { get; set; }
}
