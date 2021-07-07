using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions;
using LiteBus.Queries.Abstractions;
using LiteBus.Registry.Abstractions;

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

        private TResult SendAsync<TResult>(IMessage message)
        {
            var queryType = message.GetType();

            var descriptor = _messageRegistry.GetDescriptor(queryType);

            if (descriptor.HandlerTypes.Count > 1) throw new MultipleQueryHandlerFoundException(queryType);

            var handler = _serviceProvider.GetService(descriptor.HandlerTypes.Single()) as ISyncMessageHandler;

            return (TResult) handler.Handle(message);
        }

        public Task<TQueryResult> QueryAsync<TQueryResult>(IQuery<TQueryResult> query)
        {
            return SendAsync<Task<TQueryResult>>(query);
        }

        public IAsyncEnumerable<TQueryResult> StreamAsync<TQueryResult>(IStreamQuery<TQueryResult> query)
        {
            return SendAsync<IAsyncEnumerable<TQueryResult>>(query);
        }
    }
}