using LiteBus.Messaging.Abstractions;

namespace LiteBus.Commands.Abstractions;

/// <summary>
///     Represents a pre-handler that skips a command that produces no result because it has already been applied.
/// </summary>
/// <typeparam name="TCommand">The specific command type this shortcut runs for.</typeparam>
/// <remarks>
///     Return <see cref="Shortcut.Answer" /> when running the handler again would change nothing, which the mediation
///     reports as <see cref="MediationOutcome.Answered" /> and an audit trail records as a success. Denying the
///     command is a different decision and belongs to <see cref="ICommandGuard{TCommand}" />, which runs first. Use
///     <see cref="ICommandShortcut{TCommand,TCommandResult}" /> for a command that produces a result.
/// </remarks>
public interface ICommandShortcut<in TCommand> : IMessageShortcut<TCommand>
    where TCommand : ICommand;
