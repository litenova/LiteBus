using LiteBus.Commands.Abstractions;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Sample.Commands;

/// <summary>
///     Reports a payment command whose contents cannot be processed.
/// </summary>
/// <remarks>
///     <para>
///         A validator answers one question: is this well-formed. A negative amount is malformed input rather than a
///         refusal, so the mediation reports <see cref="MessageOutcome.Invalid" /> and an audit trail keeps it out of
///         the denial list a security review reads. Refusing an oversized payment is a different decision and lives in
///         <see cref="RequireSecondApproverGuard" />.
///     </para>
///     <para>
///         Reporting the failure is a return value rather than an exception, so a malformed message never reaches error
///         handlers as a fault. Every validator registered for the command runs and their failures are collected, so a
///         caller fixing the command sees all of them at once instead of one per round trip.
///     </para>
/// </remarks>
public sealed class ProcessPaymentCommandValidator : ICommandValidator<ProcessPaymentCommand>
{
    /// <inheritdoc />
    public Task<Validity> ValidateAsync(ProcessPaymentCommand command, CancellationToken cancellationToken = default)
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
