using System;
using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Adapts a validator that signals failure by throwing to the <see cref="Validity" /> contract.
/// </summary>
/// <typeparam name="TMessage">The message type this validator runs for.</typeparam>
/// <typeparam name="TException">The exception type the validation body throws to report failure.</typeparam>
/// <remarks>
///     <para>
///         This is migration scaffolding, and it is meant to be deleted. Before v7, a validator returned
///         <see cref="Task" /> and reported a problem by throwing, which meant the first throw won and the caller saw
///         one failure at a time. The new contract returns every failure the stage collected. The change is worth
///         making, but a codebase with a hundred validators cannot make it in one commit and still be reviewable.
///     </para>
///     <para>
///         Derive from this, move the old body into <see cref="ValidateOrThrowAsync" /> unchanged, and translate the
///         exception once in <see cref="Describe" />. Convert validators to <see cref="IMessageValidator{TMessage}" />
///         one at a time, then delete the last derived type and this base goes unused.
///     </para>
///     <para>
///         What it cannot recover is the point of the change. A validation body that throws stops at the first problem,
///         so a message with three malformed fields still reports one. Only a converted validator returning a
///         <see cref="Validity" /> built from every failure it found gives the caller all of them.
///     </para>
///     <para>
///         Use the axis-specific base instead where one exists, such as <c>ThrowingCommandValidator</c>. A validator
///         implementing only <see cref="IMessageValidator{TMessage}" /> is not a command construct and the command
///         module will refuse to register it.
///     </para>
/// </remarks>
/// <example>
///     <code><![CDATA[
/// public sealed class TransferCommandValidator : ThrowingCommandValidator<TransferCommand, ValidationException>
/// {
///     protected override Task ValidateOrThrowAsync(TransferCommand command, CancellationToken cancellationToken)
///     {
///         // The v6 body, unchanged.
///         var errors = new ErrorCollection();
///         if (command.Amount <= 0) errors.Add(nameof(command.Amount), "the amount must be positive");
///         errors.ThrowIfInvalidCommand();
///         return Task.CompletedTask;
///     }
///
///     protected override Validity Describe(ValidationException exception) =>
///         Validity.Invalid(exception.Errors.Select(e => new ValidationFailure(e.Message, e.Member)));
/// }
/// ]]></code>
/// </example>
public abstract class ThrowingValidator<TMessage, TException> : IMessageValidator<TMessage>
    where TMessage : notnull
    where TException : Exception
{
    /// <inheritdoc />
    public async Task<Validity> ValidateAsync(TMessage message, CancellationToken cancellationToken = default)
    {
        try
        {
            await ValidateOrThrowAsync(message, cancellationToken).ConfigureAwait(false);
        }
        catch (TException exception)
        {
            return Describe(exception);
        }

        return Validity.Valid;
    }

    /// <summary>
    ///     Runs the validation body, throwing <typeparamref name="TException" /> to report a failure.
    /// </summary>
    /// <param name="message">The message being mediated.</param>
    /// <param name="cancellationToken">The cancellation token supplied to the mediation operation.</param>
    /// <returns>A task that completes when the message is found well-formed.</returns>
    /// <remarks>
    ///     Only <typeparamref name="TException" /> is caught. Anything else propagates and ends the mediation as a
    ///     failure, which is correct: an unexpected exception in a validator is a fault, not a verdict about the
    ///     message.
    /// </remarks>
    protected abstract Task ValidateOrThrowAsync(TMessage message, CancellationToken cancellationToken);

    /// <summary>
    ///     Translates a caught validation exception into the failures the caller receives.
    /// </summary>
    /// <param name="exception">The exception the validation body threw.</param>
    /// <returns>A validity carrying every failure the exception describes.</returns>
    /// <remarks>
    ///     Return <see cref="Validity.Invalid(System.Collections.Generic.IEnumerable{ValidationFailure})" /> with one
    ///     failure per problem the exception carries, so a message that reaches the caller lists them all. Returning
    ///     <see cref="Validity.Valid" /> from here would swallow the failure and let a malformed message through.
    /// </remarks>
    protected abstract Validity Describe(TException exception);
}
