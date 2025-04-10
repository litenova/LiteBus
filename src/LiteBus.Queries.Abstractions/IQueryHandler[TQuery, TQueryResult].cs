using LiteBus.Messaging.Abstractions;

namespace LiteBus.Queries.Abstractions;

/// <summary>
///     Represents a handler that processes a query of type <typeparamref name="TQuery"/> and returns a result of type <typeparamref name="TQueryResult"/>.
/// </summary>
/// <typeparam name="TQuery">The type of query to handle.</typeparam>
/// <typeparam name="TQueryResult">The type of result to return after handling the query.</typeparam>
/// <remarks>
///     Query handlers are responsible for executing the business logic required to retrieve and return data.
///     When implementing this interface, the handler should process the given query and return
///     the expected result. Each query of type <typeparamref name="TQuery"/> should have exactly
///     one handler within the application. Query handlers should be side-effect free and should not
///     modify the system state.
/// </remarks>
public interface IQueryHandler<in TQuery, TQueryResult> : IRegistrableQueryConstruct, IAsyncMessageHandler<TQuery, TQueryResult> where TQuery : IQuery<TQueryResult>;