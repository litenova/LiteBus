using System;
using System.Linq;
using Autofac;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging.Abstractions;
using LiteBus.Runtime.Abstractions;
using LiteBus.Runtime.Abstractions.Hosting;
using LiteBus.Runtime.Extensions.Autofac;
using LiteBus.Runtime.Extensions.Autofac.Hosting;
using LiteBus.Runtime.Modules;

// ReSharper disable once CheckNamespace
namespace LiteBus.Extensions.Autofac;

/// <summary>
///     Extension methods for integrating LiteBus runtime with Autofac.
/// </summary>
public static class ContainerBuilderExtensions
{
    /// <summary>
    ///     Adds LiteBus to the Autofac container builder with the specified module configuration.
    /// </summary>
    /// <param name="builder">The Autofac container builder to add LiteBus to.</param>
    /// <param name="configureRegistry">Action to configure LiteBus through <see cref="IModuleRegistry" />.</param>
    /// <returns>The container builder for method chaining.</returns>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="builder" /> or
    ///     <paramref name="configureRegistry" /> is <see langword="null" />.
    /// </exception>
    /// <example>
    ///     <code>
    /// var builder = new ContainerBuilder();
    /// 
    /// builder.AddLiteBus(registry =>
    /// {
    ///     registry.AddMessageModule(messaging => messaging.RegisterFromAssembly(assembly));
    ///     registry.AddCommandModule(commands => commands.RegisterFromAssembly(assembly));
    /// });
    /// 
    /// var container = builder.Build();
    /// </code>
    /// </example>
    public static ContainerBuilder AddLiteBus(this ContainerBuilder builder,
                                              Action<IModuleRegistry> configureRegistry)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configureRegistry);

        RegisterServiceProviderAdapter(builder);

        var dependencyRegistryAdapter = new AutofacDependencyRegistryAdapter(builder);
        var moduleRegistry = new ModuleRegistry();

        configureRegistry(moduleRegistry);

        var moduleConfiguration = new ModuleConfiguration(dependencyRegistryAdapter);

        foreach (var moduleDescriptor in moduleRegistry)
        {
            moduleDescriptor.Module.Build(moduleConfiguration);
        }

        builder.RegisterBackgroundServices(moduleConfiguration.StartupTasks, moduleConfiguration.BackgroundServices);

        return builder;
    }

    /// <summary>
    ///     Adds LiteBus to the Autofac container builder with shared contract and module configuration.
    /// </summary>
    /// <param name="builder">The Autofac container builder to add LiteBus to.</param>
    /// <param name="configure">
    ///     Action to configure shared contracts and LiteBus modules through <see cref="ILiteBusBuilder" />
    ///     .
    /// </param>
    /// <returns>The container builder for method chaining.</returns>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="builder" /> or <paramref name="configure" /> is <see langword="null" />.
    /// </exception>
    public static ContainerBuilder AddLiteBus(this ContainerBuilder builder, Action<ILiteBusBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        RegisterServiceProviderAdapter(builder);

        var dependencyRegistryAdapter = new AutofacDependencyRegistryAdapter(builder);
        var moduleRegistry = new ModuleRegistry();
        var sharedContracts = new MessageContractBuilder();
        var liteBusBuilder = new LiteBusBuilder(moduleRegistry, sharedContracts);

        configure(liteBusBuilder);

        var moduleConfiguration = new ModuleConfiguration(dependencyRegistryAdapter);

        foreach (var moduleDescriptor in moduleRegistry)
        {
            moduleDescriptor.Module.Build(moduleConfiguration);
        }

        liteBusBuilder.ApplySharedContracts(moduleConfiguration);

        builder.Register(_ => new LiteBusHostManifest(
                moduleConfiguration.StartupTasks.ToList(),
                moduleConfiguration.BackgroundServices.ToList(),
                moduleConfiguration.DiagnosticChecks.ToList()))
            .As<LiteBusHostManifest>()
            .SingleInstance();

        builder.RegisterBackgroundServices(moduleConfiguration.StartupTasks, moduleConfiguration.BackgroundServices);

        return builder;
    }

    /// <summary>
    ///     Registers an <see cref="IServiceProvider" /> adapter before module build so factory registrations can resolve
    ///     services.
    /// </summary>
    /// <param name="builder">The Autofac container builder receiving the adapter registration.</param>
    private static void RegisterServiceProviderAdapter(ContainerBuilder builder)
    {
        builder.Register(c => (IServiceProvider) new AutofacServiceProviderAdapter(c.Resolve<ILifetimeScope>()))
            .As<IServiceProvider>()
            .InstancePerLifetimeScope();
    }
}