namespace LiteBus.Sample.Events;

/// <summary>
///     Reports that the sample command handler processed one payment.
/// </summary>
/// <param name="PaymentId">The processed payment identifier.</param>
/// <param name="Amount">The processed amount.</param>
public sealed record PaymentProcessed(Guid PaymentId, decimal Amount);
