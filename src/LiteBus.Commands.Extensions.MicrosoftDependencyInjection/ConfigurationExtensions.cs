using System;
using LiteBus.Messaging.Extensions.MicrosoftDependencyInjection;

namespace LiteBus.Commands.Extensions.MicrosoftDependencyInjection;

public static class ConfigurationExtensions
{
    public static ILiteBusConfiguration AddCommands(this ILiteBusConfiguration liteBusConfiguration,
                                                    Action<CommandModuleBuilder> builderAction)
    {
        liteBusConfiguration.AddModule(new CommandModule(builderAction));

        return liteBusConfiguration;
    }
}