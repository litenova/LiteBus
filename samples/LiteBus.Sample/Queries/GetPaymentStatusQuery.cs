using LiteBus.Queries.Abstractions;

namespace LiteBus.Sample.Queries;

/// <summary>
///     Requests the current in-process status of one sample payment.
/// </summary>
/// <param name="PaymentId">The payment identifier.</param>
public sealed record GetPaymentStatusQuery(Guid PaymentId) : IQuery<PaymentStatus?>;
