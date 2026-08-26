using LiteBus.Messaging.Abstractions;

namespace LiteBus.Commands.Abstractions;

/// <summary>
///     Represents a pre-handler that decides whether a command that produces no result reaches its handler.
/// </summary>
/// <typeparam name="TCommand">The specific command type this gate runs for.</typeparam>
/// <remarks>
///     Return <see cref="PipelineDirective.Deny" /> to refuse the command, which the mediation reports as
///     <see cref="MessageOutcome.Denied" /> and an audit trail records as a denial, or
///     <see cref="PipelineDirective.ShortCircuit" /> when the command has already been applied and running the handler
///     again would change nothing. Use <see cref="ICommandGate{TCommand,TCommandResult}" /> for a command that produces
///     a result.
/// </remarks>
public interface ICommandGate<in TCommand> : IMessageGate<TCommand>
    where TCommand : ICommand;
