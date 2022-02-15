using System;
using LiteBus.Messaging.Extensions.MicrosoftDependencyInjection;

namespace LiteBus.Events.Extensions.MicrosoftDependencyInjection;

public static class LiteBusConfigurationExtensions
{
    public static ILiteBusConfiguration AddEvents(this ILiteBusConfiguration liteBusConfiguration,
                                                  Action<LiteBusEventBuilder> builder)
    {
        liteBusConfiguration.AddModule(new EventsModule(builder));

        return liteBusConfiguration;
    }
}