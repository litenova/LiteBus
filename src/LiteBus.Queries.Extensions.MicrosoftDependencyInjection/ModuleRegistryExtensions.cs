using System;
using LiteBus.Messaging.Extensions.MicrosoftDependencyInjection;

namespace LiteBus.Queries.Extensions.MicrosoftDependencyInjection;

public static class ModuleRegistryExtensions
{
    public static IModuleRegistry AddQueries(this IModuleRegistry moduleRegistry,
                                             Action<QueryModuleBuilder> builder)
    {
        moduleRegistry.Register(new QueryModule(builder));

        return moduleRegistry;
    }
}