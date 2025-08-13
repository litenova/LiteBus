using System;
using Autofac;
using LiteBus.Runtime.Extensions.Autofac;
using LiteBus.Runtime.Modules;

// ReSharper disable once CheckNamespace
namespace LiteBus.Extensions.Autofac;

/// <summary>
/// Extension methods for integrating LiteBus runtime with Autofac.
/// </summary>
public static class ContainerBuilderExtensions
{
    /// <summary>
    /// Adds LiteBus to the Autofac container builder with the specified module configuration.
    /// </summary>
    /// <param name="builder">The Autofac container builder to add LiteBus to.</param>
    /// <param name="liteBusBuilderAction">Action to configure LiteBus modules.</param>
    /// <returns>The container builder for method chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="builder"/> or <paramref name="liteBusBuilderAction"/> is null.</exception>
    /// <example>
    /// <code>
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

        // Register IServiceProvider for factory compatibility
        builder.Register(c => new AutofacServiceProvider(c.Resolve<IComponentContext>()))
            .As<IServiceProvider>()
            .InstancePerLifetimeScope();

        return builder;
    }

    // Helper class to adapt Autofac's IComponentContext to IServiceProvider
    private sealed class AutofacServiceProvider(IComponentContext context) : IServiceProvider
    {
        public object? GetService(Type serviceType)
        {
            return context.ResolveOptional(serviceType);
        }
    }
}