using LiteBus.Messaging.Abstractions;

namespace LiteBus.Queries.Abstractions;

/// <summary>
///     Represents a handler that executes when an exception occurs during the processing of a specific
///     query type <typeparamref name="TQuery" />.
/// </summary>
/// <typeparam name="TQuery">The specific query type this error handler targets.</typeparam>
/// <remarks>
///     Query type-specific error handlers provide targeted exception handling for particular query types.
///     They execute when an exception occurs during the processing of the specified query type.
///     This allows for implementing specialized error handling strategies for different query types,
///     such as providing fallback data, specific error reporting, or custom recovery logic for critical queries.
/// </remarks>
public interface IQueryErrorHandler<in TQuery> : IRegistrableQueryConstruct, IAsyncMessageErrorHandler<TQuery, object> where TQuery : IQuery;