using LiteBus.Messaging.Abstractions;

namespace LiteBus.Queries.Abstractions;

/// <summary>
///     Represents a pre-handler that may stop a query pipeline before the handler runs.
/// </summary>
/// <typeparam name="TQuery">The specific query type this pre-handler runs for.</typeparam>
/// <remarks>
///     Return <see cref="PipelineDirective.ShortCircuit" /> to answer the query without running its handler, for example
///     from a cache. The mediation reports <see cref="MessageOutcome.Aborted" />.
/// </remarks>
public interface IQueryShortCircuitingPreHandler<in TQuery> : IShortCircuitingPreHandler<TQuery>
    where TQuery : IQuery;
