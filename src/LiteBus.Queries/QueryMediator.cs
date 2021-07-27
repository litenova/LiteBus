using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions;
using LiteBus.Queries.Abstractions;

namespace LiteBus.Queries
{
    public class QueryMediator : IQueryMediator
    {
        public Task<TQueryResult> QueryAsync<TQueryResult>(IQuery<TQueryResult> query,
                                                           CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public IAsyncEnumerable<TQueryResult> StreamAsync<TQueryResult>(IStreamQuery<TQueryResult> query,
                                                                        CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }
    }
}