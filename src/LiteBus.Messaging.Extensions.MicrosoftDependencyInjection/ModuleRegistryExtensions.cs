using System;

namespace LiteBus.Messaging.Extensions.MicrosoftDependencyInjection;

public static class ModuleRegistryExtensions
{
    public static IModuleRegistry AddMessaging(this IModuleRegistry moduleRegistry,
                                               Action<MessageModuleBuilder> builderAction)
    {
        moduleRegistry.Register(new MessageModule(builderAction));

        return moduleRegistry;
    }
}