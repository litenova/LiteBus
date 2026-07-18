using System;
using Autofac;
using LiteBus.Runtime.Abstractions;
using LiteBus.Runtime.Abstractions.Hosting;
using LiteBus.Runtime.Composition;
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
    ///     Adds LiteBus to the Autofac container builder through the package-neutral composition builder.
    /// </summary>
    /// <param name="builder">The Autofac container builder to add LiteBus to.</param>
    /// <param name="configure">
    ///     Action that invokes feature-specific extensions on <see cref="ILiteBusBuilder" />.
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
        RegisterDispatchScopeFactory(dependencyRegistryAdapter);
        var moduleRegistry = new ModuleRegistry();
        var liteBusBuilder = new LiteBusBuilder(moduleRegistry);

        configure(liteBusBuilder);

        var moduleConfiguration = new ModuleConfiguration(dependencyRegistryAdapter);

        foreach (var moduleDescriptor in moduleRegistry.BuildOrder())
        {
            moduleDescriptor.Module.Build(moduleConfiguration);
        }

        RegisterHostManifest(builder, moduleConfiguration);
        builder.RegisterDiagnosticChecks(moduleConfiguration.DiagnosticChecks);
        builder.RegisterBackgroundServices(moduleConfiguration.StartupTasks, moduleConfiguration.BackgroundServices);

        return builder;
    }

    /// <summary>
    ///     Registers the host manifest describing startup tasks, background services, and diagnostic probes.
    /// </summary>
    /// <param name="builder">The Autofac container builder receiving the manifest registration.</param>
    /// <param name="moduleConfiguration">The module configuration that collected host registrations.</param>
    private static void RegisterHostManifest(ContainerBuilder builder, ModuleConfiguration moduleConfiguration)
    {
        builder.Register(_ => LiteBusHostManifest.FromConfiguration(moduleConfiguration))
            .As<LiteBusHostManifest>()
            .SingleInstance();
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

    /// <summary>
    ///     Registers the Autofac dispatch-scope adapter before module validation.
    /// </summary>
    /// <param name="dependencyRegistry">The registry receiving the adapter descriptor.</param>
    private static void RegisterDispatchScopeFactory(AutofacDependencyRegistryAdapter dependencyRegistry)
    {
        dependencyRegistry.Register(new DependencyDescriptor(
            typeof(IMessageDispatchScopeFactory),
            typeof(AutofacMessageDispatchScopeFactory),
            InstanceLifetime.Singleton));
    }
}
