using LiteBus.Messaging.Abstractions;

namespace LiteBus.Queries.Abstractions;

/// <summary>
///     Represents a handler that executes when mediation of <typeparamref name="TQuery" /> ends, whatever the outcome.
/// </summary>
/// <typeparam name="TQuery">The specific query type this completion handler observes.</typeparam>
/// <remarks>
///     Use <see cref="IQueryCompletionHandler{TQuery,TQueryResult}" /> when the handler needs the query result typed.
/// </remarks>
public interface IQueryCompletionHandler<TQuery> : IMessageCompletionHandler<TQuery>
    where TQuery : IQuery;
