using LiteBus.Messaging.Abstractions;

namespace LiteBus.Commands.Abstractions;

/// <summary>
///     Represents a handler that executes when any command mediation ends, whatever the outcome.
/// </summary>
/// <remarks>
///     Command completion handlers provide a single place to observe how a command finished. Unlike post-handlers, which
///     run only on success, and error handlers, which run only for recoverable exceptions, a completion handler runs on
///     every path: success, answer, denial, invalid input, failure, and cancellation. This makes it the stage for recording an
///     audit trail, emitting metrics, or closing a unit of work.
/// </remarks>
public interface ICommandCompletionHandler : IMessageCompletionHandler<ICommand>;
