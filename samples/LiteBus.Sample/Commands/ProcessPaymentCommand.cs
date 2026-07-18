using LiteBus.Commands.Abstractions;

namespace LiteBus.Sample.Commands;

/// <summary>
///     Requests asynchronous payment processing through the durable inbox.
/// </summary>
/// <param name="PaymentId">The payment identifier.</param>
/// <param name="Amount">The payment amount.</param>
public sealed record ProcessPaymentCommand(Guid PaymentId, decimal Amount) : ICommand;
