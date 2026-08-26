using LiteBus.Messaging.Abstractions;

namespace LiteBus.Queries.Abstractions;

/// <summary>
///     Represents a pre-handler that decides whether a query is permitted to run, and can hand the caller a refusal
///     value.
/// </summary>
/// <typeparam name="TQuery">The specific query type this guard runs for.</typeparam>
/// <typeparam name="TQueryResult">The result type of the query, which the refusal value is typed over.</typeparam>
/// <remarks>
///     This contract is opt-in. <see cref="IQueryGuard{TQuery}" /> is correct here too, and refuses by raising
///     <see cref="LiteBusMessageDeniedException" />. Implement this shape when the application models failure as a
///     value, so the caller receives a failed result object instead.
/// </remarks>
public interface IQueryGuard<in TQuery, TQueryResult> : IMessageGuard<TQuery, TQueryResult>
    where TQuery : IQuery<TQueryResult>;
