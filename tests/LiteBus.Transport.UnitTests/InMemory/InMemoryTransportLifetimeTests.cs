using System.Text;
using LiteBus.Runtime.Abstractions;
using LiteBus.Runtime.Dependencies;
using LiteBus.Runtime.Modules;
using LiteBus.Transport.Abstractions;
using LiteBus.Transport.InMemory;

namespace LiteBus.Transport.UnitTests.InMemory;

/// <summary>
///     Verifies the in-memory transport module registers a singleton publisher with a transient consumer
///     and serves many envelopes from one publisher instance.
/// </summary>
public sealed class InMemoryTransportLifetimeTests
{
    /// <summary>
    ///     Verifies the publisher is a singleton and the consumer stays transient for per-subscription state.
    /// </summary>
    [Fact]
    public void Build_ShouldRegisterSingletonPublisherAndTransientConsumer()
    {
        var configuration = new ModuleConfiguration(new DependencyRegistry());

        new InMemoryTransportModule().Build(configuration);

        configuration.DependencyRegistry.Should().ContainSingle(descriptor =>
            descriptor.DependencyType == typeof(ITransportPublisher) &&
            descriptor.Lifetime == InstanceLifetime.Singleton);

        configuration.DependencyRegistry.Should().ContainSingle(descriptor =>
            descriptor.DependencyType == typeof(IMessageConsumer) &&
            descriptor.Lifetime == InstanceLifetime.Transient);
    }

    /// <summary>
    ///     Verifies one publisher instance delivers fifty envelopes to one consumer subscription.
    /// </summary>
    [Fact]
    public async Task PublishAsync_WithFiftyEnvelopesThroughOnePublisher_ShouldDeliverAll()
    {
        var broker = new InMemoryTransportBroker();
        var publisher = new InMemoryPublisher(broker, new TransportCircuitBreakerRegistry());
        var consumer = new InMemoryConsumer(broker);
        var received = 0;

        await consumer.StartAsync(
            new TransportConsumerOptions { Destination = "fifty-envelopes" },
            (message, cancellationToken) =>
            {
                Interlocked.Increment(ref received);
                return message.AcceptAsync(cancellationToken);
            }).ConfigureAwait(false);

        for (var i = 0; i < 50; i++)
        {
            await publisher.PublishAsync(new TransportPublishRequest
            {
                Destination = "fifty-envelopes",
                Body = Encoding.UTF8.GetBytes("payload")
            }).ConfigureAwait(false);
        }

        await WaitForAsync(() => received == 50, TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        received.Should().Be(50);

        await consumer.StopAsync().ConfigureAwait(false);
        await consumer.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>
    ///     Waits until the supplied condition becomes true or the timeout elapses.
    /// </summary>
    /// <param name="condition">The condition polled until it returns <see langword="true" />.</param>
    /// <param name="timeout">The maximum time to wait.</param>
    /// <returns>A task that completes when the condition becomes true.</returns>
    private static async Task WaitForAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = Environment.TickCount64 + (long) timeout.TotalMilliseconds;

        while (!condition() && Environment.TickCount64 < deadline)
        {
            await Task.Delay(10).ConfigureAwait(false);
        }
    }
}
