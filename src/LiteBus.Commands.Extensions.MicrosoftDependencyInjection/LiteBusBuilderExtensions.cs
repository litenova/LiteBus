using System;
using LiteBus.Messaging.Extensions.MicrosoftDependencyInjection;

namespace LiteBus.Commands.Extensions.MicrosoftDependencyInjection
{
    public static class LiteBusBuilderExtensions
    {
        public static ILiteBusBuilder AddCommands(this ILiteBusBuilder liteBusBuilder,
                                                  Action<LiteBusCommandBuilder> builderAction)
        {
            liteBusBuilder.AddModule(new CommandsModule(builderAction));

            return liteBusBuilder;
        }
    }
}