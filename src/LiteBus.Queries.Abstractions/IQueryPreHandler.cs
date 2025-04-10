using LiteBus.Messaging.Abstractions;

namespace LiteBus.Queries.Abstractions;

/// <summary>
///     Represents a pre-handler that executes before any query is processed.
/// </summary>
/// <remarks>
///     Query pre-handlers run before the main query handler is executed. They can be used for
///     cross-cutting concerns such as logging, validation, caching checks, or security authorization
///     that should be applied to all queries. Multiple pre-handlers can be registered in the application
///     and they will all execute before each query is handled.
/// </remarks>
public interface IQueryPreHandler : IRegistrableQueryConstruct, IAsyncMessagePreHandler<IQuery>;