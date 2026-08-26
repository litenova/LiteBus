using LiteBus.Messaging.Abstractions;

namespace LiteBus.Commands.Abstractions;

/// <summary>
///     Represents a pre-handler that decides whether a command is permitted to proceed.
/// </summary>
/// <typeparam name="TCommand">The specific command type this guard runs for.</typeparam>
/// <remarks>
///     Return <see cref="Verdict.Deny" /> to refuse the command, which the mediation reports as
///     <see cref="MessageOutcome.Denied" /> and an audit trail records as a denial, or <see cref="Verdict.Allow" /> to
///     let it proceed. This contract fits every command, including one that produces a result, because a refusal does
///     not owe the caller the value the handler would have produced. Skipping a command that has already been applied
///     is a different decision and belongs to <see cref="ICommandShortcut{TCommand}" />.
/// </remarks>
public interface ICommandGuard<in TCommand> : IMessageGuard<TCommand>
    where TCommand : ICommand;
