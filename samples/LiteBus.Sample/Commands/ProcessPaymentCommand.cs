using LiteBus.Commands.Abstractions;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Sample.Commands;

/// <summary>
///     Requests asynchronous payment processing through the durable inbox.
/// </summary>
/// <param name="PaymentId">The payment identifier.</param>
/// <param name="Amount">The payment amount.</param>
[Audited("payments.process-payment", Category = "money", TargetKind = "payment")]
public sealed record ProcessPaymentCommand(Guid PaymentId, decimal Amount) : ICommand;
