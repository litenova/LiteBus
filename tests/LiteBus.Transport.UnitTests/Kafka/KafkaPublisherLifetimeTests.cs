using LiteBus.Runtime.Abstractions;
using LiteBus.Runtime.Dependencies;
using LiteBus.Runtime.Modules;
using LiteBus.Transport.Abstractions;
using LiteBus.Transport.Kafka;

namespace LiteBus.Transport.UnitTests.Kafka;

/// <summary>
///     Verifies the Kafka publisher stays a singleton while consumers stay transient.
/// </summary>
public sealed class KafkaPublisherLifetimeTests
{
    /// <summary>
    ///     Verifies the module registers the publisher as a singleton and the consumer as transient.
    /// </summary>
    [Fact]
    public void Build_ShouldRegisterPublisherSingletonAndConsumerTransient()
    {
        var configuration = new ModuleConfiguration(new DependencyRegistry());

        new KafkaTransportModule(new KafkaTransportOptions { BootstrapServers = "localhost:9092" })
            .Build(configuration);

        configuration.DependencyRegistry.Should().ContainSingle(descriptor =>
            descriptor.DependencyType == typeof(ITransportPublisher) &&
            descriptor.Lifetime == InstanceLifetime.Singleton);

        configuration.DependencyRegistry.Should().ContainSingle(descriptor =>
            descriptor.DependencyType == typeof(IMessageConsumer) &&
            descriptor.Lifetime == InstanceLifetime.Transient);
    }

    /// <summary>
    ///     Verifies the module registers the producer owner as a singleton.
    /// </summary>
    [Fact]
    public void Build_ShouldRegisterProducerOwnerSingleton()
    {
        var configuration = new ModuleConfiguration(new DependencyRegistry());

        new KafkaTransportModule(new KafkaTransportOptions { BootstrapServers = "localhost:9092" })
            .Build(configuration);

        configuration.DependencyRegistry.Should().ContainSingle(descriptor =>
            descriptor.DependencyType == typeof(KafkaProducerOwner) &&
            descriptor.Lifetime == InstanceLifetime.Singleton);
    }

    /// <summary>
    ///     Verifies disposing the publisher never flushes or disposes the shared producer.
    /// </summary>
    [Fact]
    public void Dispose_Publisher_ShouldNotDisposeOwnerProducer()
    {
        using var owner = new KafkaProducerOwner(
            new KafkaTransportOptions { BootstrapServers = "127.0.0.1:1" });
        var publisher = new KafkaPublisher(owner, new PassiveCircuitBreakerRegistry());

        publisher.Dispose();

        owner.IsDisposed.Should().BeFalse();
    }

    /// <summary>
    ///     A circuit breaker registry that never opens, used to construct publishers without a broker.
    /// </summary>
    private sealed class PassiveCircuitBreakerRegistry : ITransportCircuitBreakerRegistry
    {
        /// <inheritdoc />
        public bool IsAnyOpen => false;

        /// <inheritdoc />
        public long FailureCount => 0;

        /// <inheritdoc />
        public ITransportCircuitBreaker GetPublisherCircuit(string destination)
        {
            return new PassiveCircuitBreaker();
        }

        /// <summary>
        ///     A circuit breaker that always admits publish operations.
        /// </summary>
        private sealed class PassiveCircuitBreaker : ITransportCircuitBreaker
        {
            /// <inheritdoc />
            public bool IsOpen => false;

            /// <inheritdoc />
            public int FailureCount => 0;

            /// <inheritdoc />
            public TransportCircuitBreakerPermit AcquirePermit()
            {
                return default;
            }

            /// <inheritdoc />
            public void RecordSuccess(TransportCircuitBreakerPermit permit)
            {
            }

            /// <inheritdoc />
            public void RecordFailure(TransportCircuitBreakerPermit permit)
            {
            }

            /// <inheritdoc />
            public void ReleasePermit(TransportCircuitBreakerPermit permit)
            {
            }
        }
    }
}
