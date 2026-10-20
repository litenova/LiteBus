using LiteBus.Messaging.Abstractions;

namespace LiteBus.Queries.Abstractions;

/// <summary>
///     Represents a handler that executes when mediation of <typeparamref name="TQuery" /> ends and receives the query
///     result typed.
/// </summary>
/// <typeparam name="TQuery">The specific query type this completion handler observes.</typeparam>
/// <typeparam name="TQueryResult">The result type of the query.</typeparam>
/// <remarks>
///     The result is present only on the paths where the pipeline produced one, which the context reports through
///     <see cref="MessageCompletionContext{TMessage,TMessageResult}.HasResult" />.
/// </remarks>
public interface IQueryCompletionHandler<TQuery, TQueryResult> : IMessageCompletionHandler<TQuery, TQueryResult>
    where TQuery : IQuery<TQueryResult>;
