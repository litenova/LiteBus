namespace LiteBus.Samples.V6.Events;

/// <summary>
///     Event stored in the outbox after a payment is processed.
/// </summary>
/// <param name="PaymentId">The payment identifier.</param>
/// <param name="Amount">The processed amount.</param>
public sealed record PaymentProcessed(Guid PaymentId, decimal Amount);
