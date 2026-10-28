using LiteBus.Runtime.Abstractions.Exceptions;
using LiteBus.Runtime.Dependencies;
using LiteBus.Runtime.Modules;
using LiteBus.Transport.InMemory;
using LiteBus.Transport.Kafka;

namespace LiteBus.Transport.UnitTests.Kafka;

/// <summary>
///     Verifies a composition hosting two transports is refused with a message naming both modules.
/// </summary>
public sealed class KafkaTransportCompositionTests
{
    /// <summary>
    ///     Verifies composing a second transport module throws and names both modules instead of an internal type.
    /// </summary>
    [Fact]
    public void Build_SecondTransportModule_ShouldNameBothModules()
    {
        var configuration = new ModuleConfiguration(new DependencyRegistry());

        new InMemoryTransportModule().Build(configuration);

        var act = () => new KafkaTransportModule(new KafkaTransportOptions
        {
            BootstrapServers = "localhost:9092"
        }).Build(configuration);

        var exception = act.Should().Throw<ModuleCompositionException>().Which;

        exception.Message.Should().Contain(nameof(InMemoryTransportModule));
        exception.Message.Should().Contain(nameof(KafkaTransportModule));
        exception.Message.Should().NotContain("TransportBrokerIdentity");
    }
}
