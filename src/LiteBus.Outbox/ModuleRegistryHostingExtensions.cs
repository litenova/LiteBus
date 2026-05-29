using System;
using LiteBus.Outbox.Hosting;
using LiteBus.Runtime.Abstractions;

namespace LiteBus.Outbox;

/// <summary>
///     Provides hosting registration extensions for the outbox module.
/// </summary>
public static class ModuleRegistryHostingExtensions
{
    /// <summary>
    ///     Registers the outbox processor <see cref="Microsoft.Extensions.Hosting.IHostedService" /> implementation.
    /// </summary>
    /// <param name="moduleRegistry">The LiteBus module registry.</param>
    /// <returns>The current module registry.</returns>
    /// <remarks>
    ///     Call <see cref="ModuleRegistryExtensions.AddOutboxModule(LiteBus.Runtime.Abstractions.IModuleRegistry, Action{OutboxModuleBuilder})" />
    ///     with <see cref="OutboxModuleBuilder.UseProcessorHost" /> before calling this method.
    /// </remarks>
    public static IModuleRegistry AddOutboxProcessorHosting(this IModuleRegistry moduleRegistry)
    {
        ArgumentNullException.ThrowIfNull(moduleRegistry);

        moduleRegistry.Register(new OutboxProcessorHostingModule());
        return moduleRegistry;
    }
}
