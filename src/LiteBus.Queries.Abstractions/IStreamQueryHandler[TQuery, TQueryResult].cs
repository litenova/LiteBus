using LiteBus.Messaging.Abstractions;

namespace LiteBus.Queries.Abstractions;

/// <summary>
///     Represents a handler that processes a stream query of type <typeparamref name="TQuery"/> and produces
///     a stream of results of type <typeparamref name="TQueryResult"/>.
/// </summary>
/// <typeparam name="TQuery">The type of stream query to handle.</typeparam>
/// <typeparam name="TQueryResult">The type of elements in the result stream.</typeparam>
/// <remarks>
///     Stream query handlers are responsible for executing the business logic required to retrieve and stream data.
///     Unlike regular query handlers that return a single result, stream query handlers produce multiple results
///     as an asynchronous enumerable. This is particularly useful for large result sets, real-time data feeds,
///     or when implementing pagination. Each stream query should have exactly one handler within the application.
///     Like regular queries, stream queries should be side-effect free and should not modify the system state.
/// </remarks>
public interface IStreamQueryHandler<in TQuery, out TQueryResult> : IRegistrableQueryConstruct, IStreamMessageHandler<TQuery, TQueryResult> where TQuery : IStreamQuery<TQueryResult>;