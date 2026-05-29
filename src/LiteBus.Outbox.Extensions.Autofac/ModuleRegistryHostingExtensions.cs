using System;
using LiteBus.Outbox.Hosting;
using LiteBus.Runtime.Abstractions;

namespace LiteBus.Outbox.Extensions.Autofac;

/// <summary>
///     Provides Autofac hosting registration extensions for the outbox module.
/// </summary>
public static class ModuleRegistryHostingExtensions
{
    /// <summary>
    ///     Registers the outbox processor hosted-service wrapper with Autofac.
    /// </summary>
    /// <param name="moduleRegistry">The LiteBus module registry.</param>
    /// <returns>The current module registry.</returns>
    /// <remarks>
    ///     The generic host must still start registered <c>IHostedService</c> instances. Call only one hosting
    ///     registration path for the outbox processor in an application.
    /// </remarks>
    public static IModuleRegistry AddOutboxProcessorHosting(this IModuleRegistry moduleRegistry)
    {
        ArgumentNullException.ThrowIfNull(moduleRegistry);

        moduleRegistry.Register(new OutboxProcessorHostingModule());
        return moduleRegistry;
    }
}
