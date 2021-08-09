using System;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Extensions.MicrosoftDependencyInjection;
using LiteBus.Queries.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LiteBus.Queries.Extensions.MicrosoftDependencyInjection
{
    internal class QueriesModule : IModule
    {
        private readonly Action<LiteBusQueryBuilder> _builder;

        public QueriesModule(Action<LiteBusQueryBuilder> builder)
        {
            _builder = builder;
        }

        public void Build(IServiceCollection services,
                          IMessageRegistry messageRegistry)
        {
            _builder(new LiteBusQueryBuilder(messageRegistry));

            services.TryAddTransient<IQueryMediator, QueryMediator>();
        }
    }
}