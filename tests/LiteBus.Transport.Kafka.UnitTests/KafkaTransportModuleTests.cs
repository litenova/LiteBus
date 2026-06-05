using AwesomeAssertions;
using LiteBus.Runtime.Dependencies;
using LiteBus.Runtime.Modules;
using LiteBus.Transport.Abstractions;
using LiteBus.Transport.Kafka;

namespace LiteBus.Transport.Kafka.UnitTests;

/// <summary>
///     Verifies Kafka transport module registration behavior.
/// </summary>
public sealed class KafkaTransportModuleTests
{
    /// <summary>
    ///     Verifies the module registers transport services once per configuration.
    /// </summary>
    [Fact]
    public void Build_ShouldRegisterTransportServicesOnce()
    {
        var configuration = new ModuleConfiguration(new DependencyRegistry());
        var options = new KafkaTransportOptions
        {
            BootstrapServers = "localhost:9092"
        };

        var module = new KafkaTransportModule(options);
        module.Build(configuration);
        module.Build(configuration);

        configuration.DependencyRegistry
            .Count(descriptor => descriptor.DependencyType == typeof(IMessageTransport))
            .Should()
            .Be(1);

        configuration.DependencyRegistry
            .Count(descriptor => descriptor.DependencyType == typeof(IMessageConsumer))
            .Should()
            .Be(1);
    }
}

