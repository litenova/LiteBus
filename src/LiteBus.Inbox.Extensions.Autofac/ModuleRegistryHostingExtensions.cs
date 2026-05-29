using System;
using LiteBus.Inbox.Hosting;
using LiteBus.Runtime.Abstractions;

namespace LiteBus.Inbox.Extensions.Autofac;

/// <summary>
///     Provides Autofac hosting registration extensions for the command inbox module.
/// </summary>
public static class ModuleRegistryHostingExtensions
{
    /// <summary>
    ///     Registers the command inbox processor hosted-service wrapper with Autofac.
    /// </summary>
    /// <param name="moduleRegistry">The LiteBus module registry.</param>
    /// <returns>The current module registry.</returns>
    /// <remarks>
    ///     The generic host must still start registered <c>IHostedService</c> instances. Call
    ///     <see cref="Inbox.ModuleRegistryHostingExtensions.AddCommandInboxProcessorHosting" /> only once across the
    ///     Microsoft or Autofac integration package you use.
    /// </remarks>
    public static IModuleRegistry AddCommandInboxProcessorHosting(this IModuleRegistry moduleRegistry)
    {
        ArgumentNullException.ThrowIfNull(moduleRegistry);

        moduleRegistry.Register(new CommandInboxProcessorHostingModule());
        return moduleRegistry;
    }
}
