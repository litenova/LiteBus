using LiteBus.Messaging.Abstractions;

namespace LiteBus.Queries.Abstractions;

/// <summary>
///     Represents a post-handler that executes after a specific query type <typeparamref name="TQuery" /> is processed,
///     with access to the query result of type <typeparamref name="TQueryResult" />.
/// </summary>
/// <typeparam name="TQuery">The specific query type this post-handler targets.</typeparam>
/// <typeparam name="TQueryResult">The type of result produced by the query.</typeparam>
/// <remarks>
///     These post-handlers execute after a query handler has processed a query and produced a result.
///     They have access to both the original query and its result, allowing for operations such as
///     result transformation, caching, logging, or other processing that depends on the query outcome.
///     Multiple type-specific post-handlers can be registered for each query type.
/// </remarks>
public interface IQueryPostHandler<in TQuery, in TQueryResult> : IRegistrableQueryConstruct, IAsyncMessagePostHandler<TQuery, TQueryResult>
    where TQuery : IQuery<TQueryResult>
    where TQueryResult : notnull;