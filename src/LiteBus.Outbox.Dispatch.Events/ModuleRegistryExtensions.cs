using System;
using LiteBus.Runtime.Abstractions;

namespace LiteBus.Outbox.Dispatch.Events;

/// <summary>
///     Provides extension methods for registering the LiteBus event outbox dispatcher.
/// </summary>
public static class ModuleRegistryExtensions
{
    /// <summary>
    ///     Registers <see cref="EventOutboxDispatcher" /> as <see cref="Outbox.Abstractions.IOutboxDispatcher" />.
    /// </summary>
    /// <param name="moduleRegistry">The module registry.</param>
    /// <returns>The current module registry.</returns>
    /// <exception cref="InvalidOperationException">
    ///     Thrown when <see cref="EventOutboxDispatchModule" /> is already registered.
    /// </exception>
    /// <remarks>
    ///     Call this after <c>AddOutboxModule</c> and <c>AddEventModule</c>. Do not register another
    ///     <see cref="Outbox.Abstractions.IOutboxDispatcher" /> when using this extension.
    /// </remarks>
    public static IModuleRegistry AddOutboxEventDispatcher(this IModuleRegistry moduleRegistry)
    {
        ArgumentNullException.ThrowIfNull(moduleRegistry);

        if (moduleRegistry.IsModuleRegistered<EventOutboxDispatchModule>())
        {
            throw new InvalidOperationException(
                "The event outbox dispatcher module is already registered. Call AddOutboxEventDispatcher only once.");
        }

        moduleRegistry.Register(new EventOutboxDispatchModule());
        return moduleRegistry;
    }
}
