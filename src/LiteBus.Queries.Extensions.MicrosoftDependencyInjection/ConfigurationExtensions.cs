using System;
using LiteBus.Messaging.Extensions.MicrosoftDependencyInjection;

namespace LiteBus.Queries.Extensions.MicrosoftDependencyInjection;

public static class ConfigurationExtensions
{
    public static ILiteBusConfiguration AddQueries(this ILiteBusConfiguration liteBusConfiguration,
                                                   Action<QueryModuleBuilder> builder)
    {
        liteBusConfiguration.AddModule(new QueryModule(builder));

        return liteBusConfiguration;
    }
}