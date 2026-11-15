using LiteBus.Commands.Abstractions;

namespace LiteBus.Sample.CommandBatches;

/// <summary>
///     Requests processing of one payment inside a command batch.
/// </summary>
/// <param name="PaymentId">The payment identifier.</param>
/// <param name="Amount">The payment amount.</param>
public sealed record ApplyBatchPaymentCommand(Guid PaymentId, decimal Amount) : ICommand;
