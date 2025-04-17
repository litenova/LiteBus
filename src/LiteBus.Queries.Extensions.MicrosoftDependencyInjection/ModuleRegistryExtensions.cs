using System;
using LiteBus.Messaging.Extensions.MicrosoftDependencyInjection;

namespace LiteBus.Queries.Extensions.MicrosoftDependencyInjection;

/// <summary>
///     Extension methods for the <see cref="IModuleRegistry" /> interface to simplify the registration of query modules.
/// </summary>
public static class ModuleRegistryExtensions
{
    /// <summary>
    ///     Adds a query module to the module registry using the provided builder action.
    /// </summary>
    /// <param name="moduleRegistry">The module registry to which the query module will be added.</param>
    /// <param name="builder">An action that configures the query module using a <see cref="QueryModuleBuilder" />.</param>
    /// <returns>The <paramref name="moduleRegistry" /> with the query module added.</returns>
    public static IModuleRegistry AddQueryModule(this IModuleRegistry moduleRegistry,
                                                 Action<QueryModuleBuilder> builder)
    {
        // Create a new QueryModule instance using the builder action and register it with the module registry.
        moduleRegistry.Register(new QueryModule(builder));

        // Return the module registry with the newly added query module.
        return moduleRegistry;
    }
}