using System;
using LiteBus.Runtime.Abstractions;

namespace LiteBus.Auditing;

/// <summary>
///     Provides extension methods for registering the auditing module.
/// </summary>
public static class ModuleRegistryExtensions
{
    /// <summary>
    ///     Registers the auditing module, or configures the registered one again.
    /// </summary>
    /// <param name="builder">The package-neutral LiteBus builder.</param>
    /// <param name="configure">The auditing configuration action.</param>
    /// <returns>The current LiteBus builder.</returns>
    /// <remarks>
    ///     Calling this twice configures one module instead of registering a second one, so several application
    ///     modules can add to one composition.
    /// </remarks>
    public static ILiteBusBuilder AddAuditing(
        this ILiteBusBuilder builder,
        Action<AuditingModuleBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        builder.Modules.AddAuditing(configure);
        return builder;
    }

    /// <summary>
    ///     Registers the auditing module, or configures the registered one again.
    /// </summary>
    /// <param name="moduleRegistry">The module registry.</param>
    /// <param name="configure">The auditing configuration action.</param>
    /// <returns>The current module registry.</returns>
    /// <remarks>
    ///     Calling this twice configures one module instead of registering a second one, so several application
    ///     modules can add to one composition.
    /// </remarks>
    public static IModuleRegistry AddAuditing(
        this IModuleRegistry moduleRegistry,
        Action<AuditingModuleBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(moduleRegistry);
        ArgumentNullException.ThrowIfNull(configure);

        var existing = moduleRegistry.Find<AuditingModule>();

        if (existing is not null)
        {
            existing.Configure(configure, child => moduleRegistry.Register(child));
            return moduleRegistry;
        }

        moduleRegistry.Register(new AuditingModule(configure));
        return moduleRegistry;
    }
}
