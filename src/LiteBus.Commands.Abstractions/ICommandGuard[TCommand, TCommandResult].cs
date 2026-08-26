using LiteBus.Messaging.Abstractions;

namespace LiteBus.Commands.Abstractions;

/// <summary>
///     Represents a pre-handler that decides whether a command that produces a result is permitted to proceed, and can
///     hand the caller a refusal value.
/// </summary>
/// <typeparam name="TCommand">The specific command type this guard runs for.</typeparam>
/// <typeparam name="TCommandResult">The result type of the command, which the refusal value is typed over.</typeparam>
/// <remarks>
///     This contract is opt-in. <see cref="ICommandGuard{TCommand}" /> is correct here too, and refuses by raising
///     <see cref="LiteBusMessageDeniedException" />. Implement this shape when the application models failure as a
///     value, so <see cref="Verdict{TCommandResult}.Deny(string,TCommandResult)" /> hands the caller a failed result
///     object instead.
/// </remarks>
public interface ICommandGuard<in TCommand, TCommandResult> : IMessageGuard<TCommand, TCommandResult>
    where TCommand : ICommand<TCommandResult>;
