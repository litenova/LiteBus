using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Queries.Abstractions;

/// <summary>
/// Represents a mediator for executing queries.
/// </summary>
public interface IQueryMediator : IRegistrableQueryConstruct
{
    /// <summary>
    /// Executes a query asynchronously and returns the result.
    /// </summary>
    /// <typeparam name="TQueryResult">The type of the query result.</typeparam>
    /// <param name="query">The query to execute.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>An asynchronous task representing the query execution and its result.</returns>
    Task<TQueryResult> QueryAsync<TQueryResult>(IQuery<TQueryResult> query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Streams the results of a query asynchronously.
    /// </summary>
    /// <typeparam name="TQueryResult">The type of the query result.</typeparam>
    /// <param name="query">The query to execute.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>An asynchronous enumerable of query results.</returns>
    IAsyncEnumerable<TQueryResult> StreamAsync<TQueryResult>(IStreamQuery<TQueryResult> query, CancellationToken cancellationToken = default);
}