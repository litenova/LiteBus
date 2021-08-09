using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.FindStrategies;
using LiteBus.Messaging.Abstractions.MediationStrategies;
using LiteBus.Queries.Abstractions;

namespace LiteBus.Queries
{
    public class QueryMediator : IQueryMediator
    {
        private readonly IMessageMediator _messageMediator;

        public QueryMediator(IMessageMediator messageMediator)
        {
            _messageMediator = messageMediator;
        }

        public Task<TQueryResult> QueryAsync<TQueryResult>(IQuery<TQueryResult> query,
                                                           CancellationToken cancellationToken = default)
        {
            var mediationStrategy =
                new SingleAsyncHandlerMediationStrategy<IQuery<TQueryResult>, TQueryResult>(cancellationToken);

            var findStrategy = new ActualTypeOrBaseTypeMessageResolveStrategy();

            return _messageMediator.Mediate(query, findStrategy, mediationStrategy);
        }

        public IAsyncEnumerable<TQueryResult> StreamAsync<TQueryResult>(IStreamQuery<TQueryResult> query,
                                                                        CancellationToken cancellationToken = default)
        {
            var mediationStrategy = new SingleStreamHandlerMediationStrategy<IStreamQuery<TQueryResult>, TQueryResult>(cancellationToken);

            var findStrategy = new ActualTypeOrBaseTypeMessageResolveStrategy();

            return _messageMediator.Mediate(query, findStrategy, mediationStrategy);
        }
    }
}