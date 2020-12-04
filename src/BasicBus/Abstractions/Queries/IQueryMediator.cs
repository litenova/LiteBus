using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace BasicBus.Abstractions
{
    public interface IQueryMediator
    {
        Task<TQueryResult> QueryAsync<TQuery, TQueryResult>(TQuery query,
                                                            CancellationToken cancellationToken = default)
            where TQuery : IQuery<TQueryResult>;

        IAsyncEnumerable<TQueryResult> StreamQueryAsync<TQuery, TQueryResult>(TQuery query,
            CancellationToken cancellationToken = default)
            where TQuery : IStreamQuery<TQueryResult>;
    }
}