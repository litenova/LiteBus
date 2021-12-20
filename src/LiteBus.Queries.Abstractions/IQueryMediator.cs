using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Queries.Abstractions
{
    /// <summary>
    ///     Mediates a query to its corresponding handler
    /// </summary>
    public interface IQueryMediator: IQueryConstruct
    {
        Task<TQueryResult> QueryAsync<TQueryResult>(IQuery<TQueryResult> query,
                                                    CancellationToken cancellationToken = default);

        IAsyncEnumerable<TQueryResult> StreamAsync<TQueryResult>(IStreamQuery<TQueryResult> query,
                                                                 CancellationToken cancellationToken = default);
    }
}