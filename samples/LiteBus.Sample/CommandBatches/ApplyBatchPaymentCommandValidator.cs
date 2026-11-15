using LiteBus.Commands.Abstractions;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Sample.CommandBatches;

/// <summary>
///     Reports a batch payment command whose contents cannot be applied.
/// </summary>
/// <remarks>
///     A malformed payment is reported as <see cref="MediationOutcome.Invalid" /> rather than thrown, so a batch that
///     contains one stops and rolls back, and <c>TrySendManyAsync</c> returns the rolled-back result with the failing
///     index instead of raising a fault.
/// </remarks>
public sealed class ApplyBatchPaymentCommandValidator : ICommandValidator<ApplyBatchPaymentCommand>
{
    /// <inheritdoc />
    public Task<Validity> ValidateAsync(ApplyBatchPaymentCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var failures = new List<ValidationFailure>();

        if (command.Amount <= 0)
        {
            failures.Add(new ValidationFailure(
                "the payment amount must be greater than zero",
                nameof(command.Amount),
                "AMOUNT_NOT_POSITIVE"));
        }

        if (command.PaymentId == Guid.Empty)
        {
            failures.Add(new ValidationFailure(
                "the payment identifier must be supplied",
                nameof(command.PaymentId),
                "PAYMENT_ID_MISSING"));
        }

        return Task.FromResult(Validity.Invalid(failures));
    }
}
