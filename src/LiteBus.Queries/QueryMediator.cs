using System;
using System.Collections.Generic;
using System.Threading;
using LiteBus.Queries.Abstractions;
using MorseCode.ITask;

namespace LiteBus.Queries
{
    public class QueryMediator : IQueryMediator
    {
        public ITask<TQueryResult> QueryAsync<TQueryResult>(IQuery<TQueryResult> query,
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