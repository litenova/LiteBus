using System;
using LiteBus.Runtime.Abstractions;
using LiteBus.Runtime.Extensions.Microsoft.DependencyInjection;
using LiteBus.Runtime.Modules;
using Microsoft.Extensions.DependencyInjection;

// ReSharper disable once CheckNamespace
namespace LiteBus.Extensions.Microsoft.DependencyInjection;

/// <summary>
/// Extension methods for integrating LiteBus runtime with Microsoft Dependency Injection.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds LiteBus to the service collection with the specified module configuration.
    /// </summary>
    /// <param name="services">The service collection to add LiteBus to.</param>
    /// <param name="liteBusBuilderAction">Action to configure LiteBus modules.</param>
    /// <returns>The service collection for method chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> or <paramref name="liteBusBuilderAction"/> is null.</exception>
    /// <example>
    /// <code>
    /// services.AddLiteBus(modules =>
    /// {
    ///     modules.AddMessageModule(messaging => messaging.RegisterFromAssembly(assembly));
    ///     modules.AddCommandModule(commands => commands.RegisterFromAssembly(assembly));
    /// });
    /// </code>
    /// </example>
    public static IServiceCollection AddLiteBus(
        this IServiceCollection services,
        Action<IModuleRegistry> liteBusBuilderAction)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(liteBusBuilderAction);

        var dependencyRegistryAdapter = new MicrosoftDependencyRegistryAdapter(services);
        var moduleRegistry = new ModuleRegistry();

        liteBusBuilderAction(moduleRegistry);

        var moduleConfiguration = new ModuleConfiguration(dependencyRegistryAdapter);

        foreach (var moduleDescriptor in moduleRegistry)
        {
            moduleDescriptor.Module.Build(moduleConfiguration);
        }

        return services;
    }
}