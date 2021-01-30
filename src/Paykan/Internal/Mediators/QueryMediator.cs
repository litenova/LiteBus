using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Paykan.Abstractions;
using Paykan.Internal.Exceptions;
using Paykan.Internal.Extensions;
using Paykan.Registry.Abstractions;

namespace Paykan.Internal.Mediators
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
            var queryType = typeof(TQuery);

            var descriptor = _messageRegistry.GetDescriptor<TQuery>();

            if (descriptor.HandlerTypes.Count > 1) throw new MultipleHandlerFoundException(queryType.Name);

            var handler = _serviceProvider
                .GetHandler<TQuery, Task<TQueryResult>>(descriptor.HandlerTypes.First());

            return handler.HandleAsync(query, cancellationToken);
        }

        public IAsyncEnumerable<TQueryResult> StreamQueryAsync<TQuery, TQueryResult>(
            TQuery query,
            CancellationToken cancellationToken = default) where TQuery : IStreamQuery<TQueryResult>
        {
            var queryType = typeof(TQuery);

            var descriptor = _messageRegistry.GetDescriptor<TQuery>();

            if (descriptor.HandlerTypes.Count > 1) throw new MultipleHandlerFoundException(queryType.Name);

            var handler = _serviceProvider
                .GetHandler<TQuery, IAsyncEnumerable<TQueryResult>>(descriptor.HandlerTypes.First());

            return handler.HandleAsync(query, cancellationToken);
        }

        public Task<TQueryResult> QueryAsync<TQueryResult>(
            IQuery<TQueryResult> query,
            CancellationToken cancellationToken = default)
        {
            var queryType = query.GetType();

            var descriptor = _messageRegistry.GetDescriptor(queryType);

            if (descriptor.HandlerTypes.Count > 1) throw new MultipleHandlerFoundException(queryType.Name);

            return _serviceProvider
                   .GetService(descriptor.HandlerTypes.First())
                   .HandleAsync<Task<TQueryResult>>(query, cancellationToken);
        }

        public IAsyncEnumerable<TQueryResult> StreamQueryAsync<TQueryResult>(IStreamQuery<TQueryResult> query,
            CancellationToken cancellationToken = default)
        {
            var queryType = query.GetType();

            var descriptor = _messageRegistry.GetDescriptor(queryType);

            if (descriptor.HandlerTypes.Count > 1) throw new MultipleHandlerFoundException(queryType.Name);

            return _serviceProvider
                   .GetService(descriptor.HandlerTypes.First())
                   .HandleAsync<IAsyncEnumerable<TQueryResult>>(query, cancellationToken);
        }
    }
}