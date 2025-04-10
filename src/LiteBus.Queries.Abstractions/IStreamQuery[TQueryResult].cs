namespace LiteBus.Queries.Abstractions;

/// <summary>
///     Represents a query that returns a stream of results of type <typeparamref name="TQueryResult"/>.
/// </summary>
/// <typeparam name="TQueryResult">The type of elements in the result stream.</typeparam>
/// <remarks>
///     Stream queries are a specialized type of query that produces multiple results over time
///     rather than a single result. They're typically used for retrieving large datasets, real-time data,
///     or implementing pagination patterns. Stream queries are handled by implementing the appropriate
///     stream query handler and are executed using the StreamAsync method of the IQueryMediator.
/// </remarks>
public interface IStreamQuery<out TQueryResult> : IQuery;