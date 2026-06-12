using LiteBus.Runtime.Abstractions;
using LiteBus.Transport.Abstractions;

namespace LiteBus.Transport;

/// <summary>
///     Shared guards for transport module <see cref="IModule.Build" /> implementations.
/// </summary>
public static class TransportModuleRegistration
{
    /// <summary>
    ///     Throws when <see cref="IMessageTransport" /> is already registered on the module configuration.
    /// </summary>
    /// <param name="configuration">The module configuration being built.</param>
    /// <param name="moduleName">The transport module type attempting registration.</param>
    public static void EnsureTransportNotRegistered(IModuleConfiguration configuration, string moduleName)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleName);

        if (configuration.DependencyRegistry.Any(descriptor => descriptor.DependencyType == typeof(IMessageTransport)))
        {
            throw new TransportAlreadyRegisteredException(moduleName);
        }
    }
}
