using LiteBus.Messaging.Abstractions;

namespace LiteBus.Queries.Abstractions;

/// <summary>
///     Represents a pre-handler that executes before a specific query type <typeparamref name="TQuery"/> is processed.
/// </summary>
/// <typeparam name="TQuery">The specific query type this pre-handler targets.</typeparam>
/// <remarks>
///     Type-specific query pre-handlers run before the main query handler for the specified query type.
///     They can be used for query-specific validation, caching, or other cross-cutting concerns that apply
///     only to the specified query type. Multiple type-specific pre-handlers can be registered for each query type
///     and will execute in sequence before the actual query handler.
/// </remarks>
public interface IQueryPreHandler<in TQuery> : IRegistrableQueryConstruct, IAsyncMessagePreHandler<TQuery> where TQuery : IQuery;