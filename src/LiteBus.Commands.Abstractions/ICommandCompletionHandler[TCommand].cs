using LiteBus.Messaging.Abstractions;

namespace LiteBus.Commands.Abstractions;

/// <summary>
///     Represents a handler that executes when mediation of <typeparamref name="TCommand" /> ends, whatever the outcome.
/// </summary>
/// <typeparam name="TCommand">The specific command type this completion handler observes.</typeparam>
/// <remarks>
///     Completion handlers run on every path: success, abort, failure, and cancellation. They observe the ending of a
///     mediation but cannot change it.
/// </remarks>
public interface ICommandCompletionHandler<TCommand> : IMessageCompletionHandler<TCommand>
    where TCommand : ICommand;
