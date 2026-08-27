using LiteBus.Messaging.Abstractions;

namespace LiteBus.Queries.Abstractions;

/// <summary>
///     Represents a pre-handler that answers a query without running its handler.
/// </summary>
/// <typeparam name="TQuery">The specific query type this shortcut runs for.</typeparam>
/// <typeparam name="TQueryResult">The result type of the query, which the answer is typed over.</typeparam>
/// <remarks>
///     Serving the query from a cache is the usual case. Return <see cref="Shortcut{TQueryResult}.Answer" /> with the
///     value the caller receives, which reports <see cref="MessageOutcome.Answered" /> and is recorded as a
///     success because nothing was refused. The framework runs this stage only after every
///     <see cref="IQueryGuard{TQuery}" /> has allowed the query, so a cached answer cannot reach a caller that a guard
///     would have refused.
/// </remarks>
public interface IQueryShortcut<in TQuery, TQueryResult> : IMessageShortcut<TQuery, TQueryResult>
    where TQuery : IQuery<TQueryResult>;
