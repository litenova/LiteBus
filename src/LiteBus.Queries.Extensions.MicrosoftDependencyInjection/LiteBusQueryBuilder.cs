using System.Reflection;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.Extensions;
using LiteBus.Queries.Abstractions;

namespace LiteBus.Queries.Extensions.MicrosoftDependencyInjection
{
    public class LiteBusQueryBuilder
    {
        private readonly IMessageRegistry _messageRegistry;
        
        public LiteBusQueryBuilder(IMessageRegistry messageRegistry)
        {
            _messageRegistry = messageRegistry;
        }

        public LiteBusQueryBuilder RegisterFrom(Assembly assembly)
        {
            _messageRegistry.Register(assembly);
            return this;
        }

        public LiteBusQueryBuilder RegisterHandler<THandler>() where THandler : IQueryHandlerBase
        {
            _messageRegistry.RegisterHandler(typeof(THandler));

            return this;
        }

        public LiteBusQueryBuilder RegisterPreHandler<TQueryPreHandler>()
            where TQueryPreHandler : IQueryPreHandlerBase
        {
            _messageRegistry.RegisterPreHandler(typeof(TQueryPreHandler));

            return this;
        }
        
        public LiteBusQueryBuilder RegisterPostHandler<TQueryPostHandler>() 
            where TQueryPostHandler : IQueryPostHandlerBase
        {
            _messageRegistry.RegisterPostHandler(typeof(TQueryPostHandler));

            return this;
        }
        
        public LiteBusQueryBuilder RegisterErrorHandler<TQueryErrorHandler>()
            where TQueryErrorHandler : IQueryErrorHandlerBase
        {
            _messageRegistry.RegisterErrorHandler(typeof(TQueryErrorHandler));

            return this;
        }
    }
}