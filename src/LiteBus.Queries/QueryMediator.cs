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
        private readonly IMessageRegistry _messageRegistry;
        private readonly IServiceProvider _serviceProvider;

        public QueryMediator(IServiceProvider serviceProvider,
                             IMessageRegistry messageRegistry)
        {
            _serviceProvider = serviceProvider;
            _messageRegistry = messageRegistry;
        }

        private IMessageHandler GetHandler(object message)
        {
            var queryType = message.GetType();

            var descriptor = _messageRegistry.GetDescriptor(queryType);

            if (descriptor.HandlerTypes.Count > 1) throw new MultipleQueryHandlerFoundException(queryType);

            var handler = _serviceProvider.GetService(descriptor.HandlerTypes.Single()) as IMessageHandler;

            return handler;
        }

        public Task<TQueryResult> QueryAsync<TQueryResult>(IQuery<TQueryResult> query,
                                                           CancellationToken cancellationToken = default)
        {
            var handler = GetHandler(query) as IAsyncMessageHandler;

            return handler.HandleAsync(query, cancellationToken) as Task<TQueryResult>;
        }

        public IAsyncEnumerable<TQueryResult> StreamAsync<TQueryResult>(IStreamQuery<TQueryResult> query,
                                                                        CancellationToken cancellationToken = default)
        {
            var handler = GetHandler(query) as IStreamMessageHandler;

            return handler.HandleAsync(query, cancellationToken) as IAsyncEnumerable<TQueryResult>;
        }
    }
}