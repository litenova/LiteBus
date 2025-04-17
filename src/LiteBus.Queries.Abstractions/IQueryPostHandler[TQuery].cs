using LiteBus.Messaging.Abstractions;

namespace LiteBus.Queries.Abstractions;

/// <summary>
///     Represents a post-handler that executes after a specific query type <typeparamref name="TQuery" /> is processed.
/// </summary>
/// <typeparam name="TQuery">The specific query type this post-handler targets.</typeparam>
/// <remarks>
///     Type-specific query post-handlers run after the main query handler for the specified query type.
///     They can be used for query-specific logging, metrics collection, or other cross-cutting concerns that apply
///     only to the specified query type. Multiple type-specific post-handlers can be registered for each query type
///     and will execute in sequence after the query handler has processed the query.
/// </remarks>
public interface IQueryPostHandler<in TQuery> : IRegistrableQueryConstruct, IAsyncMessagePostHandler<TQuery> where TQuery : IQuery;