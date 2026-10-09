using LiteBus.Runtime.Abstractions.Exceptions;
using LiteBus.Runtime.Dependencies;
using LiteBus.Runtime.Modules;
using LiteBus.Transport.Amqp;
using LiteBus.Transport.Kafka;

namespace LiteBus.Transport.UnitTests.Amqp;

/// <summary>
///     Verifies a second transport module is refused with a message naming both modules.
/// </summary>
public sealed class AmqpTransportCompositionTests
{
    /// <summary>
    ///     Verifies composing AMQP with Kafka throws and names both modules instead of an internal type.
    /// </summary>
    [Fact]
    public void Build_WithSecondTransportModule_ShouldThrowNamingBothModules()
    {
        var configuration = new ModuleConfiguration(new DependencyRegistry());
        new AmqpTransportModule(new AmqpConnectionOptions()).Build(configuration);

        var act = () => new KafkaTransportModule(new KafkaTransportOptions
        {
            BootstrapServers = "localhost:9092"
        }).Build(configuration);

        var exception = act.Should().Throw<ModuleCompositionException>().Which;
        exception.Message.Should().Contain(nameof(AmqpTransportModule));
        exception.Message.Should().Contain(nameof(KafkaTransportModule));
        exception.Message.Should().NotContain("TransportBrokerIdentity");
    }
}
