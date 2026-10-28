using LiteBus.Transport.Abstractions;
using LiteBus.Transport.InMemory;

namespace LiteBus.Transport.UnitTests.InMemory;

/// <summary>
///     Verifies the in-memory broker refuses work after disposal and the consumer treats
///     terminal channel failures as a stopped session rather than a failure.
/// </summary>
public sealed class InMemoryTransportDisposalGuardTests
{
    /// <summary>
    ///     Verifies creating an endpoint after disposal throws instead of silently reviving the broker.
    /// </summary>
    [Fact]
    public void GetOrCreateEndpoint_AfterDispose_ShouldThrowObjectDisposedException()
    {
        var broker = new InMemoryTransportBroker();
        broker.Dispose();

        var act = () => broker.GetOrCreateEndpoint("orders");

        act.Should().Throw<ObjectDisposedException>();
    }

    /// <summary>
    ///     Verifies reset after disposal still revives the broker for test isolation.
    /// </summary>
    [Fact]
    public void GetOrCreateEndpoint_AfterDisposeAndReset_ShouldAcceptNewDestinations()
    {
        var broker = new InMemoryTransportBroker();
        broker.GetOrCreateEndpoint("orders");
        broker.Dispose();
        broker.Reset();

        broker.GetOrCreateEndpoint("orders").Should().NotBeNull();
    }

    /// <summary>
    ///     Verifies releasing capacity on a disposed endpoint completes without throwing.
    /// </summary>
    [Fact]
    public void ReleaseCapacity_AfterEndpointDispose_ShouldNotThrow()
    {
        var broker = new InMemoryTransportBroker();
        var endpoint = broker.GetOrCreateEndpoint("orders");
        endpoint.Dispose();

        var act = endpoint.ReleaseCapacity;

        act.Should().NotThrow();
    }

    /// <summary>
    ///     Verifies stopping a consumer after the broker is disposed completes without throwing.
    /// </summary>
    [Fact]
    public async Task StopAsync_AfterBrokerDispose_ShouldCompleteWithoutThrowing()
    {
        var broker = new InMemoryTransportBroker();
        var consumer = new InMemoryConsumer(broker);

        await consumer.StartAsync(
            new TransportConsumerOptions { Destination = "disposed-broker" },
            static (_, _) => Task.CompletedTask).ConfigureAwait(false);

        broker.Dispose();

        var stop = () => consumer.StopAsync();
        await stop.Should().NotThrowAsync().ConfigureAwait(false);

        var wait = () => consumer.WaitUntilStoppedAsync();
        await wait.Should().NotThrowAsync().ConfigureAwait(false);

        await consumer.DisposeAsync().ConfigureAwait(false);
    }
}
