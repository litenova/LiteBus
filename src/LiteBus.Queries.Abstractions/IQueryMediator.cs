#nullable enable

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Queries.Abstractions;

/// <summary>
/// Represents the mediator interface for executing query operations within the application.
/// </summary>
public interface IQueryMediator : IRegistrableQueryConstruct
{
    /// <summary>
    /// Asynchronously executes a query and returns the result.
    /// </summary>
    /// <typeparam name="TQueryResult">The type of the result returned by the query.</typeparam>
    /// <param name="query">The query to be executed.</param>
    /// <param name="queryMediationSettings">Optional settings for query mediation.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>A task representing the asynchronous operation with a result of type <typeparamref name="TQueryResult"/>.</returns>
    Task<TQueryResult> QueryAsync<TQueryResult>(IQuery<TQueryResult> query,
                                                QueryMediationSettings? queryMediationSettings = null,
                                                CancellationToken cancellationToken = default);

    /// <summary>
    /// Asynchronously streams the results of a query.
    /// </summary>
    /// <typeparam name="TQueryResult">The type of the results returned by the stream query.</typeparam>
    /// <param name="query">The stream query to be executed.</param>
    /// <param name="queryMediationSettings">Optional settings for query mediation.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>An async enumerable of results of type <typeparamref name="TQueryResult"/>.</returns>
    IAsyncEnumerable<TQueryResult> StreamAsync<TQueryResult>(IStreamQuery<TQueryResult> query,
                                                             QueryMediationSettings? queryMediationSettings = null,
                                                             CancellationToken cancellationToken = default);
}