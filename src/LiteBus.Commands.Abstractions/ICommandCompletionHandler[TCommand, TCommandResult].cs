using LiteBus.Messaging.Abstractions;

namespace LiteBus.Commands.Abstractions;

/// <summary>
///     Represents a handler that executes when mediation of <typeparamref name="TCommand" /> ends and receives the
///     command result typed.
/// </summary>
/// <typeparam name="TCommand">The specific command type this completion handler observes.</typeparam>
/// <typeparam name="TCommandResult">The result type of the command.</typeparam>
/// <remarks>
///     The result is present only on the paths where the pipeline produced one, which the context reports through
///     <see cref="MessageCompletionContext{TMessage,TMessageResult}.HasResult" />.
/// </remarks>
public interface ICommandCompletionHandler<TCommand, TCommandResult> : IMessageCompletionHandler<TCommand, TCommandResult>
    where TCommand : ICommand<TCommandResult>;
