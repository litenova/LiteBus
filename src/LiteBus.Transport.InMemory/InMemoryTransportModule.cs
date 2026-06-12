using LiteBus.Runtime.Abstractions;
using LiteBus.Transport.Abstractions;

namespace LiteBus.Transport.InMemory;

/// <summary>
///     Module that registers in-memory transport services implementing <see cref="Abstractions.IMessageTransport" />.
/// </summary>
public sealed class InMemoryTransportModule : IModule
{
    /// <inheritdoc />
    public void Build(IModuleConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        TransportModuleRegistration.EnsureTransportNotRegistered(configuration, nameof(InMemoryTransportModule));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(InMemoryTransportBroker),
            static _ => new InMemoryTransportBroker(),
            InstanceLifetime.Singleton));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(ITransportCircuitBreaker),
            static _ => new TransportCircuitBreaker(),
            InstanceLifetime.Singleton));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IMessageTransport),
            typeof(InMemoryPublisher)));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IMessageConsumer),
            typeof(InMemoryConsumer)));

        TransportMetricsRegistration.RegisterIfNeeded(configuration, "inmemory");
    }
}
