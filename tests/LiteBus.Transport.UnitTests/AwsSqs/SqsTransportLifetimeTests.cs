using LiteBus.Runtime.Abstractions;
using LiteBus.Runtime.Abstractions.Exceptions;
using LiteBus.Runtime.Dependencies;
using LiteBus.Runtime.Modules;
using LiteBus.Transport.Abstractions;
using LiteBus.Transport.AwsSqs;
using LiteBus.Transport.InMemory;

namespace LiteBus.Transport.UnitTests.AwsSqs;

/// <summary>
///     Verifies AWS SQS transport lifetimes and the single-transport composition gate.
/// </summary>
public sealed class SqsTransportLifetimeTests
{
    /// <summary>
    ///     Verifies the publisher is a singleton while the consumer stays transient for per-subscription state.
    /// </summary>
    [Fact]
    public void Build_PublisherIsSingletonAndConsumerIsTransient()
    {
        var configuration = new ModuleConfiguration(new DependencyRegistry());

        new AwsSqsTransportModule(new AwsSqsTransportOptions { Region = "us-east-1" }).Build(configuration);

        var publisher = configuration.DependencyRegistry.Single(
            descriptor => descriptor.DependencyType == typeof(ITransportPublisher));
        publisher.ImplementationType.Should().Be<SqsPublisher>();
        publisher.Lifetime.Should().Be(InstanceLifetime.Singleton);

        var consumer = configuration.DependencyRegistry.Single(
            descriptor => descriptor.DependencyType == typeof(IMessageConsumer));
        consumer.ImplementationType.Should().Be<SqsConsumer>();
        consumer.Lifetime.Should().Be(InstanceLifetime.Transient);
    }

    /// <summary>
    ///     Verifies a second transport module is refused with a message naming both modules.
    /// </summary>
    [Fact]
    public void RegisterIfNeeded_SecondTransportModule_ThrowsNamingBothModules()
    {
        var configuration = new ModuleConfiguration(new DependencyRegistry());

        TransportMetricsRegistration.RegisterIfNeeded(configuration, "sqs", typeof(AwsSqsTransportModule));

        var act = () => TransportMetricsRegistration.RegisterIfNeeded(
            configuration,
            "inmemory",
            typeof(InMemoryTransportModule));

        var exception = act.Should().Throw<ModuleCompositionException>().Which;
        exception.Message.Should().Contain(nameof(AwsSqsTransportModule));
        exception.Message.Should().Contain(nameof(InMemoryTransportModule));
    }
}
