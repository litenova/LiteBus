namespace LiteBus.Queries.Abstractions;

/// <summary>
///     Represents a query that returns a result of type <typeparamref name="TQueryResult"/> when handled.
/// </summary>
/// <typeparam name="TQueryResult">The type of result that will be returned when the query is processed.</typeparam>
/// <remarks>
///     Queries follow the Command Query Responsibility Segregation (CQRS) pattern and represent
///     read operations that do not modify system state. They are used to retrieve data from the system
///     and return it to the caller. Each query should have exactly one handler responsible for
///     executing the query and producing the result.
/// </remarks>
public interface IQuery<TQueryResult> : IQuery;