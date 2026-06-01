using System;
using LiteBus.Runtime.Abstractions;

namespace LiteBus.Outbox.Dispatch.InProcess;

/// <summary>
///     Provides extension methods for registering the LiteBus in-process outbox dispatcher.
/// </summary>
public static class ModuleRegistryExtensions
{
    /// <summary>
    ///     Registers <see cref="InProcessOutboxDispatcher" /> as <see cref="Outbox.Abstractions.IOutboxDispatcher" />.
    /// </summary>
    /// <param name="moduleRegistry">The module registry.</param>
    /// <returns>The current module registry.</returns>
    /// <exception cref="InvalidOperationException">
    ///     Thrown when <see cref="InProcessOutboxDispatchModule" /> is already registered.
    /// </exception>
    /// <remarks>
    ///     Call this after <c>AddOutboxModule</c> and <c>AddEventModule</c>. Do not register another
    ///     <see cref="Outbox.Abstractions.IOutboxDispatcher" /> when using this extension.
    /// </remarks>
    public static IModuleRegistry AddOutboxInProcessDispatcher(this IModuleRegistry moduleRegistry)
    {
        ArgumentNullException.ThrowIfNull(moduleRegistry);

        if (moduleRegistry.IsModuleRegistered<InProcessOutboxDispatchModule>())
        {
            throw new InvalidOperationException(
                "The in-process outbox dispatcher module is already registered. Call AddOutboxInProcessDispatcher only once.");
        }

        moduleRegistry.Register(new InProcessOutboxDispatchModule());
        return moduleRegistry;
    }
}
