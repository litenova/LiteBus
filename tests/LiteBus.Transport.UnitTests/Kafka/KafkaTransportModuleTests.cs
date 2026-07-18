using LiteBus.Runtime.Abstractions.Exceptions;
using LiteBus.Runtime.Dependencies;
using LiteBus.Runtime.Modules;
using LiteBus.Transport.Abstractions;
using LiteBus.Transport.InMemory;
using LiteBus.Transport.Kafka;

namespace LiteBus.Transport.UnitTests.Kafka;

/// <summary>
///     Verifies Kafka transport module registration behavior.
/// </summary>
public sealed class KafkaTransportModuleTests
{
    /// <summary>
    ///     Verifies the module registers transport services on first build.
    /// </summary>
    [Fact]
    public void Build_ShouldRegisterTransportServices()
    {
        var configuration = new ModuleConfiguration(new DependencyRegistry());

        var options = new KafkaTransportOptions
        {
            BootstrapServers = "localhost:9092"
        };

        new KafkaTransportModule(options).Build(configuration);

        configuration.DependencyRegistry
            .Count(descriptor => descriptor.DependencyType == typeof(ITransportPublisher))
            .Should()
            .Be(1);

        configuration.DependencyRegistry
            .Count(descriptor => descriptor.DependencyType == typeof(IMessageConsumer))
            .Should()
            .Be(1);

        configuration.DiagnosticChecks.Should().ContainSingle(descriptor =>
            descriptor.ImplementationType == typeof(KafkaConnectivityDiagnosticCheck) &&
            descriptor.Name == "transport.kafka.connectivity");
    }

    /// <summary>
    ///     Verifies a second transport module throws instead of silently no-oping.
    /// </summary>
    [Fact]
    public void Build_SecondTransportModule_ShouldThrow()
    {
        var configuration = new ModuleConfiguration(new DependencyRegistry());

        new InMemoryTransportModule().Build(configuration);

        var options = new KafkaTransportOptions { BootstrapServers = "localhost:9092" };

        var act = () => new KafkaTransportModule(options).Build(configuration);

        act.Should()
            .Throw<LiteBusConfigurationException>()
            .WithMessage("*already registered*");
    }

    /// <summary>
    ///     Verifies non-positive connectivity timeouts fail during module construction.
    /// </summary>
    [Fact]
    public void Constructor_WithInvalidConnectivityTimeout_ShouldThrow()
    {
        var act = () => new KafkaTransportModule(new KafkaTransportOptions
        {
            BootstrapServers = "localhost:9092",
            ConnectivityCheckTimeout = TimeSpan.Zero
        });

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    /// <summary>
    ///     Verifies unsafe consumer group and seek backoff settings fail during module construction.
    /// </summary>
    [Fact]
    public void Constructor_WithUnsafeConsumerOptions_ShouldThrow()
    {
        Action[] actions =
        [
            () => _ = new KafkaTransportModule(new KafkaTransportOptions
            {
                BootstrapServers = "localhost:9092",
                ConsumerGroupId = " "
            }),
            () => _ = new KafkaTransportModule(new KafkaTransportOptions
            {
                BootstrapServers = "localhost:9092",
                SeekFailureBackoffInitial = TimeSpan.Zero
            }),
            () => _ = new KafkaTransportModule(new KafkaTransportOptions
            {
                BootstrapServers = "localhost:9092",
                SeekFailureBackoffInitial = TimeSpan.FromSeconds(2),
                SeekFailureBackoffMax = TimeSpan.FromSeconds(1)
            }),
            () => _ = new KafkaTransportModule(new KafkaTransportOptions
            {
                BootstrapServers = "localhost:9092",
                SeekFailureBackoffMultiplier = double.PositiveInfinity
            })
        ];

        foreach (var action in actions)
        {
            action.Should().Throw<ArgumentException>();
        }
    }
}
