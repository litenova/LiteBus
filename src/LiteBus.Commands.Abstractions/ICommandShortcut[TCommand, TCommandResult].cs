using LiteBus.Messaging.Abstractions;

namespace LiteBus.Commands.Abstractions;

/// <summary>
///     Represents a pre-handler that answers a command that produces a result, so its handler never runs.
/// </summary>
/// <typeparam name="TCommand">The specific command type this shortcut runs for.</typeparam>
/// <typeparam name="TCommandResult">The result type of the command, which the answer is typed over.</typeparam>
/// <remarks>
///     Because the handler never runs, the shortcut supplies the result the caller receives through
///     <see cref="Shortcut{TCommandResult}.Answer" />, and the compiler checks it. Returning the result an earlier
///     execution already produced is the usual case.
/// </remarks>
public interface ICommandShortcut<in TCommand, TCommandResult> : IMessageShortcut<TCommand, TCommandResult>
    where TCommand : ICommand<TCommandResult>;
