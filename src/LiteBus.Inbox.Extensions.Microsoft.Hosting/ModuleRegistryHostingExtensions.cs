using System;
using LiteBus.Runtime.Abstractions;

namespace LiteBus.Inbox.Extensions.Microsoft.Hosting;

/// <summary>
///     Provides Microsoft hosting registration extensions for the command inbox module.
/// </summary>
public static class ModuleRegistryHostingExtensions
{
    /// <summary>
    ///     Registers the command inbox processor background service for the generic host.
    /// </summary>
    /// <param name="moduleRegistry">The LiteBus module registry.</param>
    /// <param name="configure">An optional callback that configures poll interval, startup delay, and adaptive polling.</param>
    /// <returns>The current module registry.</returns>
    /// <remarks>
    ///     Call <see cref="Inbox.ModuleRegistryExtensions.AddCommandInboxModule(LiteBus.Runtime.Abstractions.IModuleRegistry, Action{CommandInboxModuleBuilder})" />
    ///     before calling this method.
    /// </remarks>
    public static IModuleRegistry AddCommandInboxProcessorHosting(
        this IModuleRegistry moduleRegistry,
        Action<CommandInboxProcessorHostOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(moduleRegistry);

        moduleRegistry.Register(new CommandInboxProcessorHostingModule(configure));
        return moduleRegistry;
    }
}
