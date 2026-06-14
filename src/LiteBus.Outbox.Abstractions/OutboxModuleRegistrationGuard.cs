using System;
using LiteBus.Runtime.Abstractions;
using LiteBus.Runtime.Abstractions.Exceptions;

namespace LiteBus.Outbox.Abstractions;

/// <summary>
///     Validates that outbox adapter modules are composed through <see cref="OutboxModule" />.
/// </summary>
public static class OutboxModuleRegistrationGuard
{
    /// <summary>
    ///     Ensures the outbox core module has registered shared services before a child module builds.
    /// </summary>
    /// <param name="configuration">The module configuration receiving the child registration.</param>
    /// <exception cref="LiteBusConfigurationException">
    ///     Thrown when <see cref="OutboxCoreRegisteredMarker" /> is absent.
    /// </exception>
    public static void EnsureCoreRegistered(IModuleConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        if (!configuration.TryGetContext<OutboxCoreRegisteredMarker>(out _))
        {
            throw new LiteBusConfigurationException(
                "Register this outbox adapter through AddOutboxModule(...). " +
                "The outbox core module must build before storage or dispatch children.");
        }
    }
}
