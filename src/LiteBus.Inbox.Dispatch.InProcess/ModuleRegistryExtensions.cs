using System;
using LiteBus.Runtime.Abstractions;
using LiteBus.Runtime.Abstractions.Exceptions;

namespace LiteBus.Inbox.Dispatch.InProcess;

/// <summary>
///     Provides extension methods for registering the LiteBus in-process inbox dispatcher.
/// </summary>
public static class ModuleRegistryExtensions
{
    /// <summary>
    ///     Registers <see cref="InProcessInboxDispatcher" /> as <see cref="Inbox.Abstractions.IInboxDispatcher" />.
    /// </summary>
    /// <param name="moduleRegistry">The module registry.</param>
    /// <returns>The current module registry.</returns>
    /// <exception cref="LiteBusConfigurationException">
    ///     Thrown when <see cref="InProcessInboxDispatchModule" /> is already registered.
    /// </exception>
    /// <remarks>
    ///     Call this after <c>AddInboxModule</c> and <c>AddCommandModule</c>. Do not register another
    ///     <see cref="Inbox.Abstractions.IInboxDispatcher" /> when using this extension.
    /// </remarks>
    [Obsolete(
        "Use AddInboxModule(i => i.UseInProcessDispatcher()) instead. " +
        "This top-level registration method will be removed in a future version.")]
    public static IModuleRegistry AddInboxInProcessDispatcher(this IModuleRegistry moduleRegistry)
    {
        ArgumentNullException.ThrowIfNull(moduleRegistry);

        if (moduleRegistry.IsModuleRegistered<InProcessInboxDispatchModule>())
        {
            throw new LiteBusConfigurationException(
                "The in-process inbox dispatcher module is already registered. Call AddInboxInProcessDispatcher only once.");
        }

        moduleRegistry.Register(new InProcessInboxDispatchModule());
        return moduleRegistry;
    }
}
