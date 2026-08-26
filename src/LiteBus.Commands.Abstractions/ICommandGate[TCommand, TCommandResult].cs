using LiteBus.Messaging.Abstractions;

namespace LiteBus.Commands.Abstractions;

/// <summary>
///     Represents a pre-handler that decides whether a command that produces a result reaches its handler.
/// </summary>
/// <typeparam name="TCommand">The specific command type this gate runs for.</typeparam>
/// <typeparam name="TCommandResult">The result type of the command, which the directive is typed over.</typeparam>
/// <remarks>
///     Because the handler never runs when the gate stops the pipeline, the gate supplies the result the caller receives,
///     and the compiler checks it. A refusal may instead be raised as
///     <see cref="LiteBusMessageDeniedException" /> by denying without a result.
/// </remarks>
public interface ICommandGate<in TCommand, TCommandResult> : IMessageGate<TCommand, TCommandResult>
    where TCommand : ICommand<TCommandResult>;
