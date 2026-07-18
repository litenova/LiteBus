namespace LiteBus.Sample.Queries;

/// <summary>
///     Describes the state returned by the sample payment query.
/// </summary>
/// <param name="PaymentId">The payment identifier.</param>
/// <param name="Amount">The processed amount.</param>
/// <param name="State">The current payment state.</param>
public sealed record PaymentStatus(Guid PaymentId, decimal Amount, string State);
