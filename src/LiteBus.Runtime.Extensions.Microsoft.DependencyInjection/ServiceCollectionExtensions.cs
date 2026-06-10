using System;
using System.Linq;
using LiteBus.Runtime.Abstractions;
using LiteBus.Runtime.Abstractions.Hosting;
using LiteBus.Runtime.Extensions.Microsoft.DependencyInjection;
using LiteBus.Runtime.Extensions.Microsoft.Hosting;
using LiteBus.Runtime.Modules;
using LiteBus.Messaging.Abstractions;
using Microsoft.Extensions.DependencyInjection;

// ReSharper disable once CheckNamespace
namespace LiteBus.Extensions.Microsoft.DependencyInjection;

/// <summary>
///     Extension methods for integrating LiteBus runtime with Microsoft Dependency Injection.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    ///     Adds LiteBus to the service collection with the specified module configuration.
    /// </summary>
    /// <param name="services">The service collection to add LiteBus to.</param>
    /// <param name="configureRegistry">Action to configure LiteBus through <see cref="IModuleRegistry" />.</param>
    /// <returns>The service collection for method chaining.</returns>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="services" /> or <paramref name="configureRegistry" /> is <see langword="null" />.
    /// </exception>
    /// <example>
    ///     <code>
    /// services.AddLiteBus(registry =>
    /// {
    ///     registry.AddMessageModule(messaging => messaging.RegisterFromAssembly(assembly));
    ///     registry.AddCommandModule(commands => commands.RegisterFromAssembly(assembly));
    /// });
    /// </code>
    /// </example>
    public static IServiceCollection AddLiteBus(
        this IServiceCollection services,
        Action<IModuleRegistry> configureRegistry)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configureRegistry);

        var dependencyRegistryAdapter = new MicrosoftDependencyRegistryAdapter(services);
        var moduleRegistry = new ModuleRegistry();

        configureRegistry(moduleRegistry);

        var moduleConfiguration = new ModuleConfiguration(dependencyRegistryAdapter);

        foreach (var moduleDescriptor in moduleRegistry)
        {
            moduleDescriptor.Module.Build(moduleConfiguration);
        }

        RegisterHostManifest(services, moduleConfiguration);
        services.RegisterBackgroundServices(moduleConfiguration.StartupTasks, moduleConfiguration.BackgroundServices);

        return services;
    }

    /// <summary>
    ///     Adds LiteBus to the service collection with shared contract and module configuration.
    /// </summary>
    /// <param name="services">The service collection to add LiteBus to.</param>
    /// <param name="configure">Action to configure shared contracts and LiteBus modules through <see cref="ILiteBusBuilder" />.</param>
    /// <returns>The service collection for method chaining.</returns>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="services" /> or <paramref name="configure" /> is <see langword="null" />.
    /// </exception>
    /// <example>
    ///     <code>
    /// services.AddLiteBus(builder =>
    /// {
    ///     builder.Contracts.Register&lt;OrderCreated&gt;("order-created", 1);
    ///     builder.Modules.AddMessageModule(messaging => messaging.RegisterFromAssembly(assembly));
    ///     builder.Modules.AddCommandModule(commands => commands.RegisterFromAssembly(assembly));
    /// });
    /// </code>
    /// </example>
    public static IServiceCollection AddLiteBus(
        this IServiceCollection services,
        Action<ILiteBusBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var dependencyRegistryAdapter = new MicrosoftDependencyRegistryAdapter(services);
        var moduleRegistry = new ModuleRegistry();
        var sharedContracts = new MessageContractBuilder();
        var builder = new LiteBusBuilder(moduleRegistry, sharedContracts);

        configure(builder);

        var moduleConfiguration = new ModuleConfiguration(dependencyRegistryAdapter);

        foreach (var moduleDescriptor in moduleRegistry)
        {
            moduleDescriptor.Module.Build(moduleConfiguration);
        }

        builder.ApplySharedContracts(moduleConfiguration);

        RegisterHostManifest(services, moduleConfiguration);
        services.RegisterBackgroundServices(moduleConfiguration.StartupTasks, moduleConfiguration.BackgroundServices);

        return services;
    }

    /// <summary>
    ///     Registers the host manifest describing startup tasks, background services, and diagnostic probes.
    /// </summary>
    /// <param name="services">The service collection receiving the manifest.</param>
    /// <param name="moduleConfiguration">The module configuration that collected host registrations.</param>
    private static void RegisterHostManifest(IServiceCollection services, ModuleConfiguration moduleConfiguration)
    {
        var manifest = new LiteBusHostManifest(
            moduleConfiguration.StartupTasks.ToList(),
            moduleConfiguration.BackgroundServices.ToList(),
            moduleConfiguration.DiagnosticChecks.ToList());

        services.AddSingleton(manifest);
    }
}
