using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Queries.Abstractions;

/// <summary>
/// Provides extension methods for <see cref="IQueryMediator"/> to simplify querying and streaming operations.
/// </summary>
public static class QueryMediatorExtensions
{
    /// <summary>
    /// Executes a query asynchronously using the specified query mediator.
    /// </summary>
    /// <typeparam name="TQueryResult">The type of the result returned by the query.</typeparam>
    /// <param name="queryMediator">The query mediator to use for executing the query.</param>
    /// <param name="query">The query to execute.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the query operation.</param>
    /// <returns>A task representing the asynchronous operation, containing the result of the query.</returns>
    /// <example>
    /// Usage example:
    /// <code>
    /// var result = await queryMediator.QueryAsync(myQuery, cancellationToken);
    /// </code>
    /// </example>
    public static Task<TQueryResult?> QueryAsync<TQueryResult>(this IQueryMediator queryMediator,
                                                               IQuery<TQueryResult> query,
                                                               CancellationToken cancellationToken = default)
    {
        return queryMediator.QueryAsync(@query, null, cancellationToken);
    }

    /// <summary>
    /// Streams query results asynchronously using the specified query mediator.
    /// </summary>
    /// <typeparam name="TQueryResult">The type of the results returned by the stream query.</typeparam>
    /// <param name="queryMediator">The query mediator to use for executing the stream query.</param>
    /// <param name="query">The stream query to execute.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the stream operation.</param>
    /// <returns>An async enumerable representing the stream of results.</returns>
    /// <example>
    /// Usage example:
    /// <code>
    /// await foreach (var item in queryMediator.StreamAsync(myStreamQuery, cancellationToken))
    /// {
    ///     // Process each item
    /// }
    /// </code>
    /// </example>
    public static IAsyncEnumerable<TQueryResult> StreamAsync<TQueryResult>(this IQueryMediator queryMediator,
                                                                           IStreamQuery<TQueryResult> query,
                                                                           CancellationToken cancellationToken = default)
    {
        return queryMediator.StreamAsync(query, null, cancellationToken);
    }

    /// <summary>
    /// Executes a tagged query asynchronously using the specified query mediator.
    /// </summary>
    /// <typeparam name="TQueryResult">The type of the result returned by the query.</typeparam>
    /// <param name="queryMediator">The query mediator to use for executing the query.</param>
    /// <param name="query">The query to execute.</param>
    /// <param name="tag">A tag that specifies the context or category of the query.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the query operation.</param>
    /// <returns>A task representing the asynchronous operation, containing the result of the query.</returns>
    /// <example>
    /// Usage example:
    /// <code>
    /// var result = await queryMediator.QueryAsync(myQuery, "UserQuery", cancellationToken);
    /// </code>
    /// </example>
    public static Task<TQueryResult?> QueryAsync<TQueryResult>(this IQueryMediator queryMediator,
                                                               IQuery<TQueryResult> query,
                                                               string tag,
                                                               CancellationToken cancellationToken = default)
    {
        return queryMediator.QueryAsync(@query,
            new QueryMediationSettings
            {
                Filters =
                {
                    Tags = [tag]
                }
            },
            cancellationToken);
    }

    /// <summary>
    /// Streams tagged query results asynchronously using the specified query mediator.
    /// </summary>
    /// <typeparam name="TQueryResult">The type of the results returned by the stream query.</typeparam>
    /// <param name="queryMediator">The query mediator to use for executing the stream query.</param>
    /// <param name="query">The stream query to execute.</param>
    /// <param name="tag">A tag that specifies the context or category of the query.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the stream operation.</param>
    /// <returns>An async enumerable representing the stream of results.</returns>
    /// <example>
    /// Usage example:
    /// <code>
    /// await foreach (var item in queryMediator.StreamAsync(myStreamQuery, "UserQueryStream", cancellationToken))
    /// {
    ///     // Process each item
    /// }
    /// </code>
    /// </example>
    public static IAsyncEnumerable<TQueryResult> StreamAsync<TQueryResult>(this IQueryMediator queryMediator,
                                                                           IStreamQuery<TQueryResult> query,
                                                                           string tag,
                                                                           CancellationToken cancellationToken = default)
    {
        return queryMediator.StreamAsync(query,
            new QueryMediationSettings
            {
                Filters =
                {
                    Tags = [tag]
                }
            },
            cancellationToken);
    }
}