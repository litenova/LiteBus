using System;
using LiteBus.Messaging.Extensions.MicrosoftDependencyInjection;

namespace LiteBus.Events.Extensions.MicrosoftDependencyInjection;

/// <summary>
///     Extension methods for the <see cref="IModuleRegistry" /> interface to simplify the registration of event modules.
/// </summary>
public static class ModuleRegistryExtensions
{
    /// <summary>
    ///     Adds an event module to the module registry using the provided builder action.
    /// </summary>
    /// <param name="moduleRegistry">The module registry to which the event module will be added.</param>
    /// <param name="builder">An action that configures the event module using an <see cref="EventModuleBuilder" />.</param>
    /// <returns>The <paramref name="moduleRegistry" /> with the event module added.</returns>
    public static IModuleRegistry AddEventModule(this IModuleRegistry moduleRegistry,
                                                 Action<EventModuleBuilder> builder)
    {
        // Create a new EventModule instance using the builder action and register it with the module registry.
        moduleRegistry.Register(new EventModule(builder));

        // Return the module registry with the newly added event module.
        return moduleRegistry;
    }
}