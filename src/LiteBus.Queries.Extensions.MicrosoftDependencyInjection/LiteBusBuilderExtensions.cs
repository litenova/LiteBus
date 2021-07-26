using System;
using LiteBus.Messaging.Extensions.MicrosoftDependencyInjection.Abstractions;

namespace LiteBus.Queries.Extensions.MicrosoftDependencyInjection
{
    public static class LiteBusBuilderExtensions
    {
        public static ILiteBusBuilder AddQueries(this ILiteBusBuilder liteBusBuilder, 
                                                 Action<LiteBusQueryBuilder> builder)
        {
            liteBusBuilder.AddModule(new QueriesModule(builder));

            return liteBusBuilder;
        }
    }
}