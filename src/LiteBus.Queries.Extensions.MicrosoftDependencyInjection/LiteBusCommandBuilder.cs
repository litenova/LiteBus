using System.Reflection;
using LiteBus.Queries.Abstractions;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.Extensions;

namespace LiteBus.Queries.Extensions.MicrosoftDependencyInjection
{
    public class LiteBusQueryBuilder
    {
        private readonly IMessageRegistry _messageRegistry;

        public LiteBusQueryBuilder(IMessageRegistry messageRegistry)
        {
            _messageRegistry = messageRegistry;
        }

        public LiteBusQueryBuilder Register(Assembly assembly)
        {
            _messageRegistry.Register(assembly);
            return this;
        }


        public LiteBusQueryBuilder RegisterHandler<THandler, TQuery, TQueryResult>()
            where THandler : IQueryHandler<TQuery, TQueryResult>
            where TQuery : IQuery<TQueryResult>
        {
            _messageRegistry.RegisterHandler(typeof(THandler));

            return this;
        }

        public LiteBusQueryBuilder RegisterStreamHandler<THandler, TQuery, TQueryResult>()
            where THandler : IStreamQueryHandler<TQuery, TQueryResult>
            where TQuery : IStreamQuery<TQueryResult>
        {
            _messageRegistry.RegisterHandler(typeof(THandler));

            return this;
        }
    }
}