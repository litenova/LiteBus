using LiteBus.Messaging.Abstractions;

namespace LiteBus.Queries.Abstractions;

/// <summary>
///     Represents a post-handler that executes after any query is processed.
/// </summary>
/// <remarks>
///     Query post-handlers run after the main query handler has completed execution.
///     They can be used for cross-cutting concerns such as logging, metrics collection,
///     or cleanup operations that should occur after every query is handled. Multiple post-handlers
///     can be registered in the application and they will all execute after each query is processed.
/// </remarks>
public interface IQueryPostHandler : IRegistrableQueryConstruct, IAsyncMessagePostHandler<IQuery>;