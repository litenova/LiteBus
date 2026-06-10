using System;
using LiteBus.Inbox.Abstractions;
using LiteBus.Messaging;
using LiteBus.Runtime.Abstractions;
using LiteBus.Runtime.Abstractions.Exceptions;

namespace LiteBus.Inbox;

/// <summary>
///     Provides extension methods for registering inbox modules.
/// </summary>
public static class ModuleRegistryExtensions
{
    /// <summary>
    ///     Registers the inbox module.
    /// </summary>
    /// <param name="moduleRegistry">The module registry.</param>
    /// <param name="builderAction">The inbox module configuration action.</param>
    /// <returns>The current module registry.</returns>
    /// <exception cref="LiteBusConfigurationException">
    ///     Thrown when <see cref="MessageModule" /> has not been registered.
    /// </exception>
    public static IModuleRegistry AddInboxModule(this IModuleRegistry moduleRegistry, Action<InboxModuleBuilder> builderAction)
    {
        ArgumentNullException.ThrowIfNull(moduleRegistry);
        ArgumentNullException.ThrowIfNull(builderAction);

        if (!moduleRegistry.IsModuleRegistered<MessageModule>())
        {
            throw new LiteBusConfigurationException(
                "MessageModule must be registered before AddInboxModule(). " +
                "Call AddMessageModule() first, or register a command, event, or query module after AddMessageModule().");
        }

        moduleRegistry.Register(new InboxModule(builderAction));
        return moduleRegistry;
    }

    /// <summary>
    ///     Registers the inbox module with default settings.
    /// </summary>
    /// <param name="moduleRegistry">The module registry.</param>
    /// <returns>The current module registry.</returns>
    /// <exception cref="LiteBusConfigurationException">
    ///     Thrown when <see cref="MessageModule" /> has not been registered.
    /// </exception>
    public static IModuleRegistry AddInboxModule(this IModuleRegistry moduleRegistry)
    {
        return AddInboxModule(moduleRegistry, _ =>
        {
        });
    }
}
