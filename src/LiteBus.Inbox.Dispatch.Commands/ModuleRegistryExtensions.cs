using System;
using LiteBus.Runtime.Abstractions;

namespace LiteBus.Inbox.Dispatch.Commands;

/// <summary>
///     Provides extension methods for registering the LiteBus command inbox dispatcher.
/// </summary>
public static class ModuleRegistryExtensions
{
    /// <summary>
    ///     Registers <see cref="CommandInboxDispatcher" /> as <see cref="Inbox.Abstractions.IInboxDispatcher" />.
    /// </summary>
    /// <param name="moduleRegistry">The module registry.</param>
    /// <returns>The current module registry.</returns>
    /// <exception cref="InvalidOperationException">
    ///     Thrown when <see cref="CommandInboxDispatchModule" /> is already registered.
    /// </exception>
    /// <remarks>
    ///     Call this after <c>AddInboxModule</c> and <c>AddCommandModule</c>. Do not register another
    ///     <see cref="Inbox.Abstractions.IInboxDispatcher" /> when using this extension.
    /// </remarks>
    public static IModuleRegistry AddInboxCommandDispatcher(this IModuleRegistry moduleRegistry)
    {
        ArgumentNullException.ThrowIfNull(moduleRegistry);

        if (moduleRegistry.IsModuleRegistered<CommandInboxDispatchModule>())
        {
            throw new InvalidOperationException(
                "The command inbox dispatcher module is already registered. Call AddInboxCommandDispatcher only once.");
        }

        moduleRegistry.Register(new CommandInboxDispatchModule());
        return moduleRegistry;
    }
}
