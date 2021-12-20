using System;
using LiteBus.Messaging.Extensions.MicrosoftDependencyInjection;

namespace LiteBus.Commands.Extensions.MicrosoftDependencyInjection
{
    public static class LiteBusConfigurationExtensions
    {
        public static ILiteBusConfiguration AddCommands(this ILiteBusConfiguration liteBusConfiguration,
                                                        Action<LiteBusCommandBuilder> builderAction)
        {
            liteBusConfiguration.AddModule(new CommandsLiteBusModule(builderAction));

            return liteBusConfiguration;
        }
    }
}