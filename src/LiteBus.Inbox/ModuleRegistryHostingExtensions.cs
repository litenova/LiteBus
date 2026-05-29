using System;
using LiteBus.Inbox.Hosting;
using LiteBus.Runtime.Abstractions;

namespace LiteBus.Inbox;

/// <summary>
///     Provides hosting registration extensions for the command inbox module.
/// </summary>
public static class ModuleRegistryHostingExtensions
{
    /// <summary>
    ///     Registers the command inbox processor <see cref="Microsoft.Extensions.Hosting.IHostedService" /> implementation.
    /// </summary>
    /// <param name="moduleRegistry">The LiteBus module registry.</param>
    /// <returns>The current module registry.</returns>
    /// <remarks>
    ///     Call <see cref="ModuleRegistryExtensions.AddCommandInboxModule(LiteBus.Runtime.Abstractions.IModuleRegistry, Action{CommandInboxModuleBuilder})" />
    ///     with <see cref="CommandInboxModuleBuilder.UseProcessorHost" /> before calling this method.
    /// </remarks>
    public static IModuleRegistry AddCommandInboxProcessorHosting(this IModuleRegistry moduleRegistry)
    {
        ArgumentNullException.ThrowIfNull(moduleRegistry);

        moduleRegistry.Register(new CommandInboxProcessorHostingModule());
        return moduleRegistry;
    }
}
