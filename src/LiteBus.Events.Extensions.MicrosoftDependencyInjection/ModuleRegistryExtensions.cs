using System;
using LiteBus.Messaging.Extensions.MicrosoftDependencyInjection;

namespace LiteBus.Events.Extensions.MicrosoftDependencyInjection;

public static class ModuleRegistryExtensions
{
    public static IModuleRegistry AddEvents(this IModuleRegistry moduleRegistry,
                                            Action<EventModuleBuilder> builder)
    {
        moduleRegistry.Register(new EventModule(builder));

        return moduleRegistry;
    }
}