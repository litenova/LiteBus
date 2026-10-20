using LiteBus.Runtime.Abstractions;
using LiteBus.Runtime.Dependencies;
using LiteBus.Runtime.Modules;
using LiteBus.Transport.Abstractions;
using LiteBus.Transport.Amqp;

namespace LiteBus.Transport.UnitTests.Amqp;

/// <summary>
///     Verifies AMQP transport module publisher and consumer lifetimes.
/// </summary>
public sealed class AmqpTransportModuleLifetimeTests
{
    /// <summary>
    ///     Verifies the publisher is a singleton so one channel is reused across dispatch scopes.
    /// </summary>
    [Fact]
    public void Build_ShouldRegisterPublisherAsSingleton()
    {
        var configuration = new ModuleConfiguration(new DependencyRegistry());

        new AmqpTransportModule(new AmqpConnectionOptions()).Build(configuration);

        configuration.DependencyRegistry.Should().ContainSingle(descriptor =>
            descriptor.DependencyType == typeof(ITransportPublisher) &&
            descriptor.ImplementationType == typeof(AmqpPublisher) &&
            descriptor.Lifetime == InstanceLifetime.Singleton);
    }

    /// <summary>
    ///     Verifies the consumer is transient so each subscription gets its own channel state.
    /// </summary>
    [Fact]
    public void Build_ShouldRegisterConsumerAsTransient()
    {
        var configuration = new ModuleConfiguration(new DependencyRegistry());

        new AmqpTransportModule(new AmqpConnectionOptions()).Build(configuration);

        configuration.DependencyRegistry.Should().ContainSingle(descriptor =>
            descriptor.DependencyType == typeof(IMessageConsumer) &&
            descriptor.ImplementationType == typeof(AmqpConsumer) &&
            descriptor.Lifetime == InstanceLifetime.Transient);
    }
}
