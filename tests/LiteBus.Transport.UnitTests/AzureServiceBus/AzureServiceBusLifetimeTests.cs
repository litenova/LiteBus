using LiteBus.Runtime.Abstractions;
using LiteBus.Runtime.Abstractions.Exceptions;
using LiteBus.Runtime.Dependencies;
using LiteBus.Runtime.Modules;
using LiteBus.Transport.Abstractions;
using LiteBus.Transport.AzureServiceBus;
using LiteBus.Transport.InMemory;

namespace LiteBus.Transport.UnitTests.AzureServiceBus;

/// <summary>
///     Verifies Azure Service Bus transport lifetimes and single-transport composition.
/// </summary>
public sealed class AzureServiceBusLifetimeTests
{
    /// <summary>
    ///     Verifies the publisher is a singleton and the consumer is transient.
    /// </summary>
    [Fact]
    public void Build_ShouldRegisterPublisherAsSingletonAndConsumerAsTransient()
    {
        var configuration = new ModuleConfiguration(new DependencyRegistry());

        new AzureServiceBusTransportModule(new AzureServiceBusTransportOptions
        {
            ConnectionString = "Endpoint=sb://example.servicebus.windows.net/;SharedAccessKeyName=test;SharedAccessKey=key"
        }).Build(configuration);

        configuration.DependencyRegistry.Should().ContainSingle(descriptor =>
            descriptor.DependencyType == typeof(ITransportPublisher) &&
            descriptor.Lifetime == InstanceLifetime.Singleton);

        configuration.DependencyRegistry.Should().ContainSingle(descriptor =>
            descriptor.DependencyType == typeof(IMessageConsumer) &&
            descriptor.Lifetime == InstanceLifetime.Transient);
    }

    /// <summary>
    ///     Verifies a second transport module is refused with a message naming both modules.
    /// </summary>
    [Fact]
    public void Build_AzureThenInMemory_ShouldRefuseSecondTransportNamingBoth()
    {
        var configuration = new ModuleConfiguration(new DependencyRegistry());

        new AzureServiceBusTransportModule(new AzureServiceBusTransportOptions
        {
            ConnectionString = "Endpoint=sb://example.servicebus.windows.net/;SharedAccessKeyName=test;SharedAccessKey=key"
        }).Build(configuration);

        var act = () => new InMemoryTransportModule().Build(configuration);

        var exception = act.Should().Throw<LiteBusConfigurationException>().Which;
        exception.Message.Should().Contain(nameof(AzureServiceBusTransportModule));
        exception.Message.Should().Contain(nameof(InMemoryTransportModule));
        exception.Message.Should().NotContain("TransportBrokerIdentity");
    }
}
