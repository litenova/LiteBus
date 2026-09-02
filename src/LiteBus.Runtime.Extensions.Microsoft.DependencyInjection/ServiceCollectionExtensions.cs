using System;
using LiteBus.Runtime.Abstractions;
using LiteBus.Runtime.Abstractions.Hosting;
using LiteBus.Runtime.Composition;
using LiteBus.Runtime.Extensions.Microsoft.DependencyInjection;
using LiteBus.Runtime.Extensions.Microsoft.Hosting;
using LiteBus.Runtime.Modules;
using Microsoft.Extensions.DependencyInjection;

// ReSharper disable once CheckNamespace
namespace LiteBus.Extensions.Microsoft.DependencyInjection;

/// <summary>
///     Extension methods for integrating LiteBus runtime with Microsoft Dependency Injection.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    ///     Adds LiteBus to the service collection through the package-neutral composition builder.
    /// </summary>
    /// <param name="services">The service collection to add LiteBus to.</param>
    /// <param name="configure">
    ///     Action that invokes feature-specific extensions on <see cref="ILiteBusBuilder" />.
    /// </param>
    /// <returns>The service collection for method chaining.</returns>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="services" /> or <paramref name="configure" /> is <see langword="null" />.
    /// </exception>
    /// <example>
    ///     <code>
    /// services.AddLiteBus(builder =>
    /// {
    ///     builder.AddMessaging(messaging =>
    ///         messaging.Contracts.Register&lt;OrderCreated&gt;("order-created", 1));
    ///     builder.AddCommands(commands => commands.RegisterFromAssembly(assembly));
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
        RegisterDispatchScopeFactory(dependencyRegistryAdapter);
        var moduleRegistry = new ModuleRegistry();
        var builder = new LiteBusBuilder(moduleRegistry);

        configure(builder);

        var moduleConfiguration = new ModuleConfiguration(dependencyRegistryAdapter);

        foreach (var moduleDescriptor in moduleRegistry.BuildOrder())
        {
            moduleDescriptor.Module.Build(moduleConfiguration);
        }

        // Rules spanning several modules can only be checked once every module has registered what it owns.
        foreach (var validate in moduleConfiguration.CompositionValidations)
        {
            validate();
        }

        RegisterHostManifest(services, moduleConfiguration);
        services.RegisterDiagnosticChecks(moduleConfiguration.DiagnosticChecks);
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
        services.AddSingleton(LiteBusHostManifest.FromConfiguration(moduleConfiguration));
    }

    /// <summary>
    ///     Registers the Microsoft dependency injection dispatch-scope adapter before module validation.
    /// </summary>
    /// <param name="dependencyRegistry">The registry receiving the adapter descriptor.</param>
    private static void RegisterDispatchScopeFactory(MicrosoftDependencyRegistryAdapter dependencyRegistry)
    {
        dependencyRegistry.Register(new DependencyDescriptor(
            typeof(IMessageDispatchScopeFactory),
            typeof(MicrosoftMessageDispatchScopeFactory),
            InstanceLifetime.Singleton));
    }
}
