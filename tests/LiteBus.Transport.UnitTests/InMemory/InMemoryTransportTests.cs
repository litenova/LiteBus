using System.Text;
using System.Threading.Channels;
using LiteBus.Transport.Abstractions;
using LiteBus.Transport.InMemory;

namespace LiteBus.Transport.UnitTests.InMemory;

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
        var publisher = new InMemoryPublisher(broker, new TransportCircuitBreakerRegistry());
        var consumer = new InMemoryConsumer(broker);
        TransportMessage? received = null;

        await consumer.StartAsync(
            new TransportConsumerOptions { Destination = "orders" },
            (message, _) =>
            {
                received = message;
                return message.AcceptAsync();
            }).ConfigureAwait(false);

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
        }).ConfigureAwait(false);

        await WaitForAsync(() => received is not null, TimeSpan.FromSeconds(2)).ConfigureAwait(false);

        received.Should().NotBeNull();
        received!.Route.Should().Be("ship");
        received.CorrelationId.Should().Be("corr-1");
        Encoding.UTF8.GetString(received.Body.Span).Should().Contain("orderId");
        received.Headers[TransportHeaders.ContractName].Should().Be("orders.commands.ship");

        await consumer.StopAsync().ConfigureAwait(false);
        await consumer.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>
    ///     Verifies rejected deliveries can be returned to the in-memory queue.
    /// </summary>
    [Fact]
    public async Task ReturnToQueue_ShouldRedeliverMessage()
    {
        var broker = new InMemoryTransportBroker();
        var publisher = new InMemoryPublisher(broker, new TransportCircuitBreakerRegistry());
        var consumer = new InMemoryConsumer(broker);
        var deliveryCount = 0;

        await consumer.StartAsync(
            new TransportConsumerOptions { Destination = "retry-queue" },
            async (message, _) =>
            {
                deliveryCount++;

                if (deliveryCount == 1)
                {
                    await message.ReturnToQueueAsync().ConfigureAwait(false);
                    return;
                }

                await message.AcceptAsync().ConfigureAwait(false);
            }).ConfigureAwait(false);

        await publisher.PublishAsync(new TransportPublishRequest
        {
            Destination = "retry-queue",
            Body = Encoding.UTF8.GetBytes("payload")
        }).ConfigureAwait(false);

        await WaitForAsync(() => deliveryCount >= 2, TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        deliveryCount.Should().BeGreaterThanOrEqualTo(2);

        await consumer.StopAsync().ConfigureAwait(false);
        await consumer.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>
    ///     Verifies handler exceptions requeue the delivery by default instead of dropping it.
    /// </summary>
    [Fact]
    public async Task HandlerThrow_ShouldRequeueMessageByDefault()
    {
        var broker = new InMemoryTransportBroker();
        var publisher = new InMemoryPublisher(broker, new TransportCircuitBreakerRegistry());
        var consumer = new InMemoryConsumer(broker);
        var deliveryCount = 0;

        await consumer.StartAsync(
            new TransportConsumerOptions { Destination = "fault-queue" },
            (_, _) =>
            {
                deliveryCount++;
                throw new InvalidOperationException("handler failed");
            }).ConfigureAwait(false);

        await publisher.PublishAsync(new TransportPublishRequest
        {
            Destination = "fault-queue",
            Body = Encoding.UTF8.GetBytes("payload")
        }).ConfigureAwait(false);

        await WaitForAsync(() => deliveryCount >= 2, TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        deliveryCount.Should().BeGreaterThanOrEqualTo(2);

        await consumer.StopAsync().ConfigureAwait(false);
        await consumer.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>
    ///     Verifies a full destination blocks publication until the admitted delivery settles.
    /// </summary>
    [Fact]
    public async Task PublishAsync_WhenDestinationIsFull_ShouldWaitForSettlement()
    {
        var broker = new InMemoryTransportBroker(
            new InMemoryTransportOptions { DestinationCapacity = 1 });
        var publisher = new InMemoryPublisher(broker, new TransportCircuitBreakerRegistry());
        var consumer = new InMemoryConsumer(broker);
        var deliveryCount = 0;

        await publisher.PublishAsync(CreateRequest("bounded-destination")).ConfigureAwait(false);
        var waitingPublish = publisher.PublishAsync(CreateRequest("bounded-destination"));

        await Task.Delay(50).ConfigureAwait(false);
        waitingPublish.IsCompleted.Should().BeFalse();

        await consumer.StartAsync(
            new TransportConsumerOptions { Destination = "bounded-destination" },
            async (message, _) =>
            {
                deliveryCount++;

                if (deliveryCount == 1)
                {
                    await message.ReturnToQueueAsync().ConfigureAwait(false);
                    return;
                }

                await message.AcceptAsync().ConfigureAwait(false);
            }).ConfigureAwait(false);

        await waitingPublish.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        await WaitForAsync(() => deliveryCount >= 3, TimeSpan.FromSeconds(2)).ConfigureAwait(false);

        deliveryCount.Should().BeGreaterThanOrEqualTo(3);

        await consumer.StopAsync().ConfigureAwait(false);
        await consumer.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>
    ///     Verifies canceling a capacity wait does not consume a destination reservation.
    /// </summary>
    [Fact]
    public async Task PublishAsync_WhenCapacityWaitIsCanceled_ShouldReleaseAdmissionWaiter()
    {
        var broker = new InMemoryTransportBroker(
            new InMemoryTransportOptions { DestinationCapacity = 1 });
        var publisher = new InMemoryPublisher(broker, new TransportCircuitBreakerRegistry());
        var consumer = new InMemoryConsumer(broker);

        await publisher.PublishAsync(CreateRequest("cancel-bounded-destination")).ConfigureAwait(false);
        using var cancellationSource = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        var act = () => publisher.PublishAsync(
            CreateRequest("cancel-bounded-destination"),
            cancellationSource.Token);

        await act.Should().ThrowAsync<OperationCanceledException>().ConfigureAwait(false);

        var deliveryCount = 0;

        await consumer.StartAsync(
            new TransportConsumerOptions { Destination = "cancel-bounded-destination" },
            async (message, _) =>
            {
                deliveryCount++;
                await message.AcceptAsync().ConfigureAwait(false);
            }).ConfigureAwait(false);

        await WaitForAsync(() => deliveryCount == 1, TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        await publisher.PublishAsync(CreateRequest("cancel-bounded-destination"))
            .WaitAsync(TimeSpan.FromSeconds(2))
            .ConfigureAwait(false);
        await WaitForAsync(() => deliveryCount == 2, TimeSpan.FromSeconds(2)).ConfigureAwait(false);

        deliveryCount.Should().Be(2);

        await consumer.StopAsync().ConfigureAwait(false);
        await consumer.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>
    ///     Verifies an open circuit is surfaced without recording a second broker failure.
    /// </summary>
    [Fact]
    public async Task PublishAsync_WithOpenCircuit_ShouldRethrowCircuitException()
    {
        var circuitBreaker = new ThrowingCircuitBreaker(new TransportCircuitBreakerOpenException());
        var publisher = new InMemoryPublisher(
            new InMemoryTransportBroker(),
            new FixedCircuitBreakerRegistry(circuitBreaker));

        var act = () => publisher.PublishAsync(CreateRequest("open-circuit"));

        await act.Should().ThrowAsync<TransportCircuitBreakerOpenException>().ConfigureAwait(false);
        circuitBreaker.RecordedFailures.Should().Be(0);
    }

    /// <summary>
    ///     Verifies publishing to a closed destination records a circuit-breaker failure.
    /// </summary>
    [Fact]
    public async Task PublishAsync_WithClosedDestination_ShouldRecordFailure()
    {
        var broker = new InMemoryTransportBroker();
        broker.GetOrCreateEndpoint("closed-destination").TryComplete().Should().BeTrue();
        var circuitBreakerRegistry = new TransportCircuitBreakerRegistry();
        var circuitBreaker = circuitBreakerRegistry.GetPublisherCircuit("closed-destination");
        var publisher = new InMemoryPublisher(broker, circuitBreakerRegistry);

        var act = () => publisher.PublishAsync(CreateRequest("closed-destination"));

        await act.Should().ThrowAsync<ChannelClosedException>().ConfigureAwait(false);
        circuitBreaker.FailureCount.Should().Be(1);
    }

    /// <summary>
    ///     Verifies an unexpected application failure is rethrown without poisoning broker connectivity state.
    /// </summary>
    [Fact]
    public async Task PublishAsync_WithUnexpectedFailure_ShouldRethrowWithoutRecordingBrokerFailure()
    {
        var circuitBreaker = new ThrowingCircuitBreaker(
            new InvalidOperationException("unexpected"),
            throwDuringAcquire: false);
        var publisher = new InMemoryPublisher(
            new InMemoryTransportBroker(),
            new FixedCircuitBreakerRegistry(circuitBreaker));

        var act = () => publisher.PublishAsync(CreateRequest("unexpected-failure"));

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("unexpected").ConfigureAwait(false);
        circuitBreaker.RecordedFailures.Should().Be(0);
        circuitBreaker.ReleasedPermits.Should().Be(1);
    }

    private static TransportPublishRequest CreateRequest(string destination)
    {
        return new TransportPublishRequest
        {
            Destination = destination,
            Body = Encoding.UTF8.GetBytes("payload")
        };
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

    private sealed class ThrowingCircuitBreaker : ITransportCircuitBreaker
    {
        private readonly Exception _exception;
        private readonly bool _throwDuringAcquire;

        public ThrowingCircuitBreaker(Exception exception, bool throwDuringAcquire = true)
        {
            _exception = exception;
            _throwDuringAcquire = throwDuringAcquire;
        }

        public bool IsOpen => _exception is TransportCircuitBreakerOpenException;

        public int FailureCount => RecordedFailures;

        public int RecordedFailures { get; private set; }

        public int ReleasedPermits { get; private set; }

        public TransportCircuitBreakerPermit AcquirePermit()
        {
            if (_throwDuringAcquire)
            {
                throw _exception;
            }

            return default;
        }

        public void RecordSuccess(TransportCircuitBreakerPermit permit)
        {
            if (!_throwDuringAcquire)
            {
                throw _exception;
            }
        }

        public void RecordFailure(TransportCircuitBreakerPermit permit)
        {
            RecordedFailures++;
        }

        public void ReleasePermit(TransportCircuitBreakerPermit permit)
        {
            ReleasedPermits++;
        }
    }

    private sealed class FixedCircuitBreakerRegistry : ITransportCircuitBreakerRegistry
    {
        private readonly ITransportCircuitBreaker _circuitBreaker;

        public FixedCircuitBreakerRegistry(ITransportCircuitBreaker circuitBreaker)
        {
            _circuitBreaker = circuitBreaker;
        }

        public bool IsAnyOpen => _circuitBreaker.IsOpen;

        public long FailureCount => _circuitBreaker.FailureCount;

        public ITransportCircuitBreaker GetPublisherCircuit(string destination)
        {
            return _circuitBreaker;
        }
    }
}
