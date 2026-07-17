using System;
using LiteBus.Runtime.Abstractions;
using LiteBus.Runtime.Abstractions.Exceptions;

namespace LiteBus.Inbox.Abstractions;

/// <summary>
///     Validates that inbox adapter modules are composed through <c>InboxModule</c>.
/// </summary>
public static class InboxModuleRegistrationGuard
{
    /// <summary>
    ///     Ensures the inbox core module has registered shared services before a child module builds.
    /// </summary>
    /// <param name="configuration">The module configuration receiving the child registration.</param>
    /// <exception cref="LiteBusConfigurationException">
    ///     Thrown when <see cref="InboxCoreRegisteredMarker" /> is absent.
    /// </exception>
    public static void EnsureCoreRegistered(IModuleConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        if (!configuration.TryGetContext<InboxCoreRegisteredMarker>(out _))
        {
            throw new LiteBusConfigurationException(
                "Register this inbox adapter through AddInboxModule(...). " +
                "The inbox core module must build before storage, dispatch, or ingress children.");
        }
    }
}
