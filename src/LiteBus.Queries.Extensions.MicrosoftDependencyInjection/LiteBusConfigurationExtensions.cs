using System;
using LiteBus.Messaging.Extensions.MicrosoftDependencyInjection;

namespace LiteBus.Queries.Extensions.MicrosoftDependencyInjection;

public static class LiteBusConfigurationExtensions
{
    public static ILiteBusConfiguration AddQueries(this ILiteBusConfiguration liteBusConfiguration,
                                                   Action<LiteBusQueryBuilder> builder)
    {
        liteBusConfiguration.AddModule(new QueriesModule(builder));

        return liteBusConfiguration;
    }
}