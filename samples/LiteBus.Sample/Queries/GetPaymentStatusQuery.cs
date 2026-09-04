using LiteBus.Messaging.Abstractions;
using LiteBus.Queries.Abstractions;

namespace LiteBus.Sample.Queries;

/// <summary>
///     Requests the current in-process status of one sample payment.
/// </summary>
/// <param name="PaymentId">The payment identifier.</param>
[AuditExempt("reading a payment status returns no personal data")]
public sealed record GetPaymentStatusQuery(Guid PaymentId) : IQuery<PaymentStatus?>;
