using System.Text;
using AwesomeAssertions;
using LiteBus.Transport.Abstractions;
using LiteBus.Transport;

namespace LiteBus.Transport.InMemory.UnitTests;

/// <summary>
///     Verifies publish and consume behavior for the in-memory channel transport.
/// </summary>
public sealed class InMemoryTransportTests
{
    /// <summary>
    ///     Verifies a published message is delivered to a consumer on the same destination.
    /// </summary>
    [Fact]
    public async Task PublishAndConsume_ShouldDeliverMessageToConsumer()
    {
        var broker = new InMemoryTransportBroker();
        var publisher = new InMemoryPublisher(broker, new TransportCircuitBreaker());
        var consumer = new InMemoryConsumer(broker);
        TransportMessage? received = null;

        await consumer.StartAsync(
            new TransportConsumerOptions { Destination = "orders" },
            (message, _) =>
            {
                received = message;
                return message.AcceptAsync();
            });

        await publisher.PublishAsync(new TransportPublishRequest
        {
            Destination = "orders",
            Route = "ship",
            Body = Encoding.UTF8.GetBytes("""{"orderId":"1"}"""),
            MessageId = Guid.NewGuid().ToString("D"),
            CorrelationId = "corr-1",
            Headers = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [TransportHeaders.ContractName] = "orders.commands.ship"
            }
        });

        await WaitForAsync(() => received is not null, TimeSpan.FromSeconds(2));

        received.Should().NotBeNull();
        received!.Route.Should().Be("ship");
        received.CorrelationId.Should().Be("corr-1");
        Encoding.UTF8.GetString(received.Body.Span).Should().Contain("orderId");
        received.Headers[TransportHeaders.ContractName].Should().Be("orders.commands.ship");

        await consumer.StopAsync();
        await consumer.DisposeAsync();
    }

    /// <summary>
    ///     Verifies rejected deliveries can be returned to the in-memory queue.
    /// </summary>
    [Fact]
    public async Task ReturnToQueue_ShouldRedeliverMessage()
    {
        var broker = new InMemoryTransportBroker();
        var publisher = new InMemoryPublisher(broker, new TransportCircuitBreaker());
        var consumer = new InMemoryConsumer(broker);
        var deliveryCount = 0;

        await consumer.StartAsync(
            new TransportConsumerOptions { Destination = "retry-queue" },
            async (message, _) =>
            {
                deliveryCount++;

                if (deliveryCount == 1)
                {
                    await message.ReturnToQueueAsync();
                    return;
                }

                await message.AcceptAsync();
            });

        await publisher.PublishAsync(new TransportPublishRequest
        {
            Destination = "retry-queue",
            Body = Encoding.UTF8.GetBytes("payload")
        });

        await WaitForAsync(() => deliveryCount >= 2, TimeSpan.FromSeconds(2));
        deliveryCount.Should().BeGreaterThanOrEqualTo(2);

        await consumer.StopAsync();
        await consumer.DisposeAsync();
    }

    /// <summary>
    ///     Waits until the supplied condition becomes true or the timeout elapses.
    /// </summary>
    /// <param name="condition">The condition polled until it returns <see langword="true" />.</param>
    /// <param name="timeout">The maximum time to wait.</param>
    /// <returns>A task that completes when the condition becomes true.</returns>
    private static async Task WaitForAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = Environment.TickCount64 + (long)timeout.TotalMilliseconds;

        while (!condition() && Environment.TickCount64 < deadline)
        {
            await Task.Delay(10);
        }
    }
}

