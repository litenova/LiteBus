using System;
using LiteBus.Messaging.Extensions.MicrosoftDependencyInjection;

namespace LiteBus.Commands.Extensions.MicrosoftDependencyInjection;

/// <summary>
///     Extension methods for the <see cref="IModuleRegistry" /> interface to simplify the registration of command modules.
/// </summary>
public static class ModuleRegistryExtensions
{
    /// <summary>
    ///     Adds a command module to the module registry using the provided builder action.
    /// </summary>
    /// <param name="moduleRegistry">The module registry to which the command module will be added.</param>
    /// <param name="builderAction">An action that configures the command module using a <see cref="CommandModuleBuilder" />.</param>
    /// <returns>The <paramref name="moduleRegistry" /> with the command module added.</returns>
    public static IModuleRegistry AddCommandModule(this IModuleRegistry moduleRegistry,
                                                   Action<CommandModuleBuilder> builderAction)
    {
        // Create a new CommandModule instance using the builder action and register it with the module registry.
        moduleRegistry.Register(new CommandModule(builderAction));

        // Return the module registry with the newly added command module.
        return moduleRegistry;
    }
}