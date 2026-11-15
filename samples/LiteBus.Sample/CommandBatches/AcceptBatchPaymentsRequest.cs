namespace LiteBus.Sample.CommandBatches;

/// <summary>
///     Carries the payment entries accepted by the sample batch endpoint.
/// </summary>
/// <param name="Payments">The payments sent as one command batch, in list order.</param>
public sealed record AcceptBatchPaymentsRequest(IReadOnlyList<BatchPaymentEntry> Payments);
