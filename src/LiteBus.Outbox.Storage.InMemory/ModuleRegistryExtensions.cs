using System;
using LiteBus.Runtime.Abstractions;

namespace LiteBus.Outbox.Storage.InMemory;

/// <summary>
///     Provides extension methods for registering in-memory outbox storage.
/// </summary>
public static class ModuleRegistryExtensions
{
    /// <summary>
    ///     Registers a process-local in-memory outbox store for tests and local development.
    /// </summary>
    /// <param name="moduleRegistry">The module registry.</param>
    /// <returns>The current module registry.</returns>
    public static IModuleRegistry AddInMemoryOutboxStorage(this IModuleRegistry moduleRegistry)
    {
        ArgumentNullException.ThrowIfNull(moduleRegistry);

        moduleRegistry.Register(new InMemoryOutboxStorageModule());
        return moduleRegistry;
    }
}
