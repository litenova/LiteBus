using LiteBus.Messaging.Abstractions;

namespace LiteBus.Queries.Abstractions;

/// <summary>
///     Represents a pre-handler that decides whether a query reaches its handler.
/// </summary>
/// <typeparam name="TQuery">The specific query type this gate runs for.</typeparam>
/// <typeparam name="TQueryResult">The result type of the query, which the directive is typed over.</typeparam>
/// <remarks>
///     Return <see cref="PipelineDirective{TQueryResult}.ShortCircuit" /> to answer the query without running its
///     handler, for example from a cache. That reports <see cref="MessageOutcome.ShortCircuited" />, which an audit trail
///     records as a success, because nothing was refused. Return one of the <c>Deny</c> overloads to refuse the query
///     instead, which reports <see cref="MessageOutcome.Denied" /> and is recorded as a denial.
/// </remarks>
public interface IQueryGate<in TQuery, TQueryResult> : IMessageGate<TQuery, TQueryResult>
    where TQuery : IQuery<TQueryResult>;
