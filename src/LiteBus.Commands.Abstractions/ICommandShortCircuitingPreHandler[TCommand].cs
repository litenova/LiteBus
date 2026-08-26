using LiteBus.Messaging.Abstractions;

namespace LiteBus.Commands.Abstractions;

/// <summary>
///     Represents a pre-handler that may stop a command pipeline before the handler runs.
/// </summary>
/// <typeparam name="TCommand">The specific command type this pre-handler runs for.</typeparam>
/// <remarks>
///     Return <see cref="PipelineDirective.ShortCircuit" /> to refuse or satisfy the command without running its
///     handler. The mediation reports <see cref="MessageOutcome.Aborted" />, and an audit trail records the reason.
/// </remarks>
public interface ICommandShortCircuitingPreHandler<in TCommand> : IShortCircuitingPreHandler<TCommand>
    where TCommand : ICommand;
