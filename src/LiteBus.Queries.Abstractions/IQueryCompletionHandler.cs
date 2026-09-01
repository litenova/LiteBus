using LiteBus.Messaging.Abstractions;

namespace LiteBus.Queries.Abstractions;

/// <summary>
///     Represents a handler that executes when any query mediation ends, whatever the outcome.
/// </summary>
/// <remarks>
///     Query completion handlers run on every path: success, answer, denial, invalid input, failure, and cancellation. They are
///     the stage for recording that a read happened, which post-handlers cannot do because they never run when a query is
///     refused.
/// </remarks>
public interface IQueryCompletionHandler : IMessageCompletionHandler<IQuery>;
