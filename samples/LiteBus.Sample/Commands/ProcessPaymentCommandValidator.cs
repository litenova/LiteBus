using LiteBus.Commands.Abstractions;

namespace LiteBus.Sample.Commands;

/// <summary>
///     Rejects a payment command that cannot be processed.
/// </summary>
/// <remarks>
///     <see cref="ICommandValidator{TCommand}" /> is a named wrapper over <c>ICommandPreHandler</c>. Throwing here stops
///     the pipeline before the main handler runs, and the completion stage records the failure.
/// </remarks>
public sealed class ProcessPaymentCommandValidator : ICommandValidator<ProcessPaymentCommand>
{
    /// <inheritdoc />
    public Task ValidateAsync(ProcessPaymentCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.Amount <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(command),
                command.Amount,
                "Payment amount must be greater than zero.");
        }

        return Task.CompletedTask;
    }
}
