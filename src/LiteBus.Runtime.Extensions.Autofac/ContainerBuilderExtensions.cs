using System;
using System.Linq;
using Autofac;
using Autofac.Extensions.DependencyInjection;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
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
    /// <param name="liteBusBuilderAction">Action to configure LiteBus modules.</param>
    /// <returns>The container builder for method chaining.</returns>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="builder" /> or
    ///     <paramref name="liteBusBuilderAction" /> is <see langword="null" />.
    /// </exception>
    /// <example>
    ///     <code>
    /// var builder = new ContainerBuilder();
    /// 
    /// builder.AddLiteBus(modules =>
    /// {
    ///     modules.AddMessageModule(messaging => messaging.RegisterFromAssembly(assembly));
    ///     modules.AddCommandModule(commands => commands.RegisterFromAssembly(assembly));
    /// });
    /// 
    /// var container = builder.Build();
    /// </code>
    /// </example>
    public static ContainerBuilder AddLiteBus(this ContainerBuilder builder,
                                              Action<IModuleRegistry> liteBusBuilderAction)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(liteBusBuilderAction);

        var dependencyRegistryAdapter = new AutofacDependencyRegistryAdapter(builder);
        var moduleRegistry = new ModuleRegistry();

        liteBusBuilderAction(moduleRegistry);

        var moduleConfiguration = new ModuleConfiguration(dependencyRegistryAdapter);

        foreach (var moduleDescriptor in moduleRegistry)
        {
            moduleDescriptor.Module.Build(moduleConfiguration);
        }

        builder.RegisterBackgroundServices(moduleConfiguration.StartupTasks, moduleConfiguration.BackgroundServices);

        builder.Register(c => new AutofacServiceProvider(c.Resolve<ILifetimeScope>()))
            .As<IServiceProvider>()
            .InstancePerLifetimeScope();

        return builder;
    }

    /// <summary>
    ///     Adds LiteBus to the Autofac container builder with shared contract and module configuration.
    /// </summary>
    /// <param name="builder">The Autofac container builder to add LiteBus to.</param>
    /// <param name="configure">Action to configure shared contracts and LiteBus modules through <see cref="ILiteBusBuilder" />.</param>
    /// <returns>The container builder for method chaining.</returns>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="builder" /> or <paramref name="configure" /> is <see langword="null" />.
    /// </exception>
    public static ContainerBuilder AddLiteBus(this ContainerBuilder builder, Action<ILiteBusBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

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

        builder.Register(c => new AutofacServiceProvider(c.Resolve<ILifetimeScope>()))
            .As<IServiceProvider>()
            .InstancePerLifetimeScope();

        return builder;
    }
}