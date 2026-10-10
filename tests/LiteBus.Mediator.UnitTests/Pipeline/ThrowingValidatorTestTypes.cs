using LiteBus.Commands.Abstractions;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Mediator.UnitTests.Pipeline;

/// <summary>
///     The exception a pre-v7 validation body throws to report a failure.
/// </summary>
public sealed class LegacyValidationException : Exception
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="LegacyValidationException" /> class.
    /// </summary>
    /// <param name="member">The member the failure is about.</param>
    /// <param name="message">The failure message.</param>
    public LegacyValidationException(string member, string message)
        : base(message)
    {
        Member = member;
    }

    /// <summary>
    ///     Gets the member the failure is about.
    /// </summary>
    public string Member { get; }
}

/// <summary>
///     A command with two fields, so an adapted validator and a converted one can each fail on their own.
/// </summary>
internal sealed class RemitCommand : ICommand
{
    /// <summary>
    ///     Gets or sets the amount to transfer. A negative amount fails validation and zero raises an unexpected fault.
    /// </summary>
    public decimal Amount { get; set; } = 1;

    /// <summary>
    ///     Gets or sets the transfer reference. An empty reference fails the converted validator.
    /// </summary>
    public string Reference { get; set; } = "ref";
}

/// <summary>
///     Handles <see cref="RemitCommand" />.
/// </summary>
internal sealed class RemitCommandHandler : ICommandHandler<RemitCommand>
{
    /// <inheritdoc />
    public Task HandleAsync(RemitCommand message, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}

/// <summary>
///     A validator whose body still throws, adapted by the migration base.
/// </summary>
internal sealed class LegacyRemitCommandValidator
    : ThrowingCommandValidator<RemitCommand, LegacyValidationException>
{
    /// <inheritdoc />
    protected override Task ValidateOrThrowAsync(RemitCommand message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (message.Amount == 0)
        {
            // Stands in for a genuine fault inside a validator, which must not be reported as a verdict.
            throw new InvalidOperationException("ledger unavailable");
        }

        if (message.Amount < 0)
        {
            throw new LegacyValidationException(nameof(message.Amount), "the amount must be positive");
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    protected override Validity Describe(LegacyValidationException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return Validity.Invalid(exception.Message, exception.Member);
    }
}

/// <summary>
///     A validator already converted to the v7 contract, running beside the adapted one.
/// </summary>
internal sealed class ConvertedRemitCommandValidator : ICommandValidator<RemitCommand>
{
    /// <inheritdoc />
    public Task<Validity> ValidateAsync(RemitCommand message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        return Task.FromResult(string.IsNullOrWhiteSpace(message.Reference)
            ? Validity.Invalid("a reference is required", nameof(message.Reference))
            : Validity.Valid);
    }
}
