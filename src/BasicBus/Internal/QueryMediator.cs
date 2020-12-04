using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BasicBus.Abstractions;
using BasicBus.Internal.Extensions;

namespace BasicBus.Internal
{
    internal class QueryMediator : IQueryMediator
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly IMessageRegistry _messageRegistry;

        public QueryMediator(IServiceProvider serviceProvider,
                             IMessageRegistry messageRegistry)
        {
            _serviceProvider = serviceProvider;
            _messageRegistry = messageRegistry;
        }
        
        public Task<TQueryResult> QueryAsync<TQuery, TQueryResult>(TQuery query,
                                                                   CancellationToken cancellationToken = default)
            where TQuery : IQuery<TQueryResult>
        {
            var descriptor = _messageRegistry[query.GetType()];

            var handler = _serviceProvider.GetHandler<TQuery, Task<TQueryResult>>(descriptor.HandlerType);

            return handler.HandleAsync(query, cancellationToken);
        }

        public IAsyncEnumerable<TQueryResult> StreamQueryAsync<TQuery, TQueryResult>(
            TQuery query,
            CancellationToken cancellationToken = default) where TQuery : IStreamQuery<TQueryResult>
        {
            var descriptor = _messageRegistry[query.GetType()];

            var handler = _serviceProvider.GetHandler<TQuery, IAsyncEnumerable<TQueryResult>>(descriptor.HandlerType);

            return handler.HandleAsync(query, cancellationToken);
        }
    }
}