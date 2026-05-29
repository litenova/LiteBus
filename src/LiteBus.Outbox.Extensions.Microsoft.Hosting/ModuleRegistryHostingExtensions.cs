using System;
using LiteBus.Runtime.Abstractions;

namespace LiteBus.Outbox.Extensions.Microsoft.Hosting;

/// <summary>
///     Provides Microsoft hosting registration extensions for the outbox module.
/// </summary>
public static class ModuleRegistryHostingExtensions
{
    /// <summary>
    ///     Registers the outbox processor background service for the generic host.
    /// </summary>
    /// <param name="moduleRegistry">The LiteBus module registry.</param>
    /// <param name="configure">An optional callback that configures poll interval, startup delay, and adaptive polling.</param>
    /// <returns>The current module registry.</returns>
    /// <remarks>
    ///     Call <see cref="Outbox.ModuleRegistryExtensions.AddOutboxModule(LiteBus.Runtime.Abstractions.IModuleRegistry, Action{OutboxModuleBuilder})" />
    ///     before calling this method.
    /// </remarks>
    public static IModuleRegistry AddOutboxProcessorHosting(
        this IModuleRegistry moduleRegistry,
        Action<OutboxProcessorHostOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(moduleRegistry);

        moduleRegistry.Register(new OutboxProcessorHostingModule(configure));
        return moduleRegistry;
    }
}
