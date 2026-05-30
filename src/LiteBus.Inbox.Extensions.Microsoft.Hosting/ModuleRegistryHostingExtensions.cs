using System;
using LiteBus.Runtime.Abstractions;

namespace LiteBus.Inbox.Extensions.Microsoft.Hosting;

/// <summary>
///     Provides Microsoft hosting registration extensions for the inbox module.
/// </summary>
public static class ModuleRegistryHostingExtensions
{
    /// <summary>
    ///     Registers the inbox processor background service for the generic host.
    /// </summary>
    /// <param name="moduleRegistry">The LiteBus module registry.</param>
    /// <param name="configure">An optional callback that configures poll interval, startup delay, and adaptive polling.</param>
    /// <returns>The current module registry.</returns>
    /// <remarks>
    ///     Call <see cref="Inbox.ModuleRegistryExtensions.AddInboxModule(LiteBus.Runtime.Abstractions.IModuleRegistry, Action{InboxModuleBuilder})" />
    ///     before calling this method.
    /// </remarks>
    public static IModuleRegistry AddInboxProcessorHosting(
        this IModuleRegistry moduleRegistry,
        Action<InboxProcessorHostOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(moduleRegistry);

        moduleRegistry.Register(new InboxProcessorHostingModule(configure));
        return moduleRegistry;
    }
}
