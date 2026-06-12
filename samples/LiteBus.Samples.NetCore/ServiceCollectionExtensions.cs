using LiteBus.Commands;
using LiteBus.Events;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Queries;
using LiteBus.Samples.Commands;

namespace LiteBus.Samples.NetCore;

public static class ServiceCollectionExtensions
{
    /// <summary>
    ///     Registers the Order Processing example with Microsoft Dependency Injection.
    ///     This includes all commands, queries, event handlers, and infrastructure services.
    /// </summary>
    public static IServiceCollection AddLiteBusExample(this IServiceCollection services)
    {
        // Register LiteBus with all modules
        services.AddLiteBus(registry =>
        {
            var exampleAssembly = typeof(PlaceOrderCommandHandler).Assembly;

            registry.AddMessageModule(_ =>
            {
            });

            registry.AddCommandModule(module => module.RegisterFromAssembly(exampleAssembly));
            registry.AddQueryModule(module => module.RegisterFromAssembly(exampleAssembly));
            registry.AddEventModule(module => module.RegisterFromAssembly(exampleAssembly));
        });

        return services;
    }
}