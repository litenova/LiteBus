using System;
using LiteBus.Messaging.Extensions.MicrosoftDependencyInjection;

namespace LiteBus.Commands.Extensions.MicrosoftDependencyInjection;

public static class ModuleRegistryExtensions
{
    public static IModuleRegistry AddCommands(this IModuleRegistry moduleRegistry,
                                              Action<CommandModuleBuilder> builderAction)
    {
        moduleRegistry.Register(new CommandModule(builderAction));

        return moduleRegistry;
    }
}