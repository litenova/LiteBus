using LiteBus.Messaging.Abstractions;

namespace LiteBus.Queries.Abstractions;

/// <summary>
///     Represents a handler that executes when an exception occurs during the processing of a specific
///     query type <typeparamref name="TQuery" /> that returns <typeparamref name="TQueryResult" />.
/// </summary>
/// <typeparam name="TQuery">The specific query type this error handler targets.</typeparam>
/// <typeparam name="TQueryResult">The result type produced by the query handler.</typeparam>
/// <remarks>
///     Typed query error handlers can set <see cref="MessageErrorContext.Outcome" /> and
///     <see cref="MessageErrorContext.HandledResult" /> to suppress recoverable exceptions and return a fallback result.
/// </remarks>
public interface IQueryErrorHandler<in TQuery, TQueryResult>
    : IAsyncMessageErrorHandler<TQuery, TQueryResult>
    where TQuery : IQuery<TQueryResult>;
