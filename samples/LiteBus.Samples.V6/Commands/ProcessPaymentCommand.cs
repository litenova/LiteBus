using LiteBus.Commands.Abstractions;

namespace LiteBus.Samples.V6.Commands;

/// <summary>
///     Command accepted into the inbox for deferred payment processing.
/// </summary>
/// <param name="PaymentId">The payment identifier.</param>
/// <param name="Amount">The payment amount.</param>
public sealed record ProcessPaymentCommand(Guid PaymentId, decimal Amount) : ICommand;
