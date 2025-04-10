using LiteBus.Messaging.Abstractions;

namespace LiteBus.Queries.Abstractions;

/// <summary>
///     Represents a handler that executes when an exception occurs during any query processing.
/// </summary>
/// <remarks>
///     Query error handlers provide centralized exception handling for the query pipeline.
///     They execute when any exception occurs during query processing (in pre-handlers, handlers, or post-handlers).
///     Multiple error handlers can be registered to implement different error handling strategies such as
///     logging, providing fallback results, or custom recovery logic for all queries.
/// </remarks>
public interface IQueryErrorHandler : IRegistrableQueryConstruct, IAsyncMessageErrorHandler<IQuery, object>;