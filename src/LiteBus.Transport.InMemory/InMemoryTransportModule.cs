using LiteBus.Runtime.Abstractions;
using LiteBus.Transport.Abstractions;

namespace LiteBus.Transport.InMemory;

/// <summary>
///     Module that registers in-memory transport services implementing <see cref="Abstractions.ITransportPublisher" />.
/// </summary>
public sealed class InMemoryTransportModule : IModule
{
    /// <summary>
    ///     Gets the process-local transport settings configured by the application.
    /// </summary>
    private readonly InMemoryTransportOptions _options;

    /// <summary>
    ///     Initializes a new instance of the <see cref="InMemoryTransportModule" /> class with default options.
    /// </summary>
    public InMemoryTransportModule()
        : this(new InMemoryTransportOptions())
    {
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="InMemoryTransportModule" /> class.
    /// </summary>
    /// <param name="options">The process-local transport settings.</param>
    public InMemoryTransportModule(InMemoryTransportOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.DestinationCapacity, 1);
        _options = options;
    }

    /// <inheritdoc />
    public void Build(IModuleConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(InMemoryTransportOptions),
            _options));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(InMemoryTransportBroker),
            static serviceProvider =>
            {
                var options = serviceProvider.GetService(typeof(InMemoryTransportOptions))
                                  as InMemoryTransportOptions ??
                              throw new InvalidOperationException(
                                  $"{nameof(InMemoryTransportOptions)} is not registered.");

                return new InMemoryTransportBroker(options);
            },
            InstanceLifetime.Singleton));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(ITransportCircuitBreakerRegistry),
            static _ => new TransportCircuitBreakerRegistry(),
            InstanceLifetime.Singleton));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(ITransportPublisher),
            typeof(InMemoryPublisher)));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IMessageConsumer),
            typeof(InMemoryConsumer)));

        TransportMetricsRegistration.RegisterIfNeeded(configuration, "inmemory");
    }
}
