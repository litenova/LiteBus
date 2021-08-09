using System.Collections.Generic;
using System.Threading;
using MorseCode.ITask;

namespace LiteBus.Queries.Abstractions
{
    /// <summary>
    ///     Mediates a query to its corresponding handler
    /// </summary>
    public interface IQueryMediator
    {
        ITask<TQueryResult> QueryAsync<TQueryResult>(IQuery<TQueryResult> command,
                                                     CancellationToken cancellationToken = default);

        IAsyncEnumerable<TQueryResult> StreamAsync<TQueryResult>(IStreamQuery<TQueryResult> query,
                                                                 CancellationToken cancellationToken = default);
    }
}