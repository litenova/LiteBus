namespace LiteBus.Sample;

/// <summary>
///     Carries the payment fields accepted by the sample HTTP endpoint.
/// </summary>
/// <param name="PaymentId">The caller-supplied payment identifier.</param>
/// <param name="Amount">The positive payment amount.</param>
public sealed record AcceptPaymentRequest(Guid PaymentId, decimal Amount);
