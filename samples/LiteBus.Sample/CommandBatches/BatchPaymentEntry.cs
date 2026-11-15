namespace LiteBus.Sample.CommandBatches;

/// <summary>
///     Carries one payment entry of the sample batch endpoint request.
/// </summary>
/// <param name="PaymentId">The caller-supplied payment identifier.</param>
/// <param name="Amount">The positive payment amount.</param>
public sealed record BatchPaymentEntry(Guid PaymentId, decimal Amount);
