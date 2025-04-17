using System.Threading;
using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Commands.Abstractions;

/// <summary>
///     Represents a validator that performs validation on a specific command type <typeparamref name="TCommand"/> before it is processed.
/// </summary>
/// <typeparam name="TCommand">The specific command type this validator targets.</typeparam>
/// <remarks>
///     ICommandValidator is a specialized wrapper around ICommandPreHandler that simplifies implementing validation logic.
///     It provides a more semantic API through the ValidateAsync method while internally mapping to the PreHandleAsync method.
///     Command validators run before the main command handler and can be used to ensure commands meet business rules
///     and contain valid data before processing. Multiple validators can be registered for each command type.
///     
///     If validation fails, implementations should throw an exception to prevent the command from being processed further.
/// </remarks>
public interface ICommandValidator<in TCommand> : ICommandPreHandler<TCommand> where TCommand : ICommand
{
    Task IAsyncMessagePreHandler<TCommand>.PreHandleAsync(TCommand message, CancellationToken cancellationToken)
    {
        return ValidateAsync(message, cancellationToken);
    }

    /// <summary>
    ///     Validates the command
    /// </summary>
    /// <param name="command">The command to validate</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task representing the asynchronous operation</returns>
    Task ValidateAsync(TCommand command, CancellationToken cancellationToken = default);
}