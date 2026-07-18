using LiteBus.Transport.Amqp;
using RabbitMQ.Client;

namespace LiteBus.Transport.UnitTests.Amqp;

/// <summary>
///     Verifies AMQP publisher circuit scoping and cancellation behavior.
/// </summary>
public sealed class AmqpPublisherTests
{
    /// <summary>
    ///     Verifies the valid empty-name default exchange uses its routing key as the circuit destination.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task PublishAsync_WithDefaultExchange_ShouldScopeCircuitByRoutingKey()
    {
        var registry = new RecordingCircuitBreakerRegistry();
        var publisher = new AmqpPublisher(new UnexpectedConnectionManager(), registry);
        await using (publisher.ConfigureAwait(false))
        {
            var act = () => publisher.PublishAsync(new AmqpPublishRequest
            {
                Exchange = string.Empty,
                RoutingKey = "orders.commands",
                Body = ReadOnlyMemory<byte>.Empty
            });

            await act.Should().ThrowAsync<TransportCircuitBreakerOpenException>().ConfigureAwait(false);
            registry.Destination.Should().Be("amqp:default:orders.commands");
        }
    }

    /// <summary>
    ///     Verifies a named exchange remains the circuit destination across its routing keys.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task PublishAsync_WithNamedExchange_ShouldScopeCircuitByExchange()
    {
        var registry = new RecordingCircuitBreakerRegistry();
        var publisher = new AmqpPublisher(new UnexpectedConnectionManager(), registry);
        await using (publisher.ConfigureAwait(false))
        {
            var act = () => publisher.PublishAsync(new AmqpPublishRequest
            {
                Exchange = "orders.events",
                RoutingKey = "orders.created",
                Body = ReadOnlyMemory<byte>.Empty
            });

            await act.Should().ThrowAsync<TransportCircuitBreakerOpenException>().ConfigureAwait(false);
            registry.Destination.Should().Be("amqp:exchange:orders.events");
        }
    }

    /// <summary>
    ///     Verifies caller cancellation takes precedence over circuit lookup and broker access.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task PublishAsync_WhenCallerAlreadyCanceled_ShouldStopBeforeCircuitLookup()
    {
        var registry = new RecordingCircuitBreakerRegistry();
        var publisher = new AmqpPublisher(new UnexpectedConnectionManager(), registry);
        await using (publisher.ConfigureAwait(false))
        {
            using var cancellationSource = new CancellationTokenSource();
            await cancellationSource.CancelAsync().ConfigureAwait(false);

            var act = () => publisher.PublishAsync(
                new AmqpPublishRequest
                {
                    Exchange = string.Empty,
                    RoutingKey = "orders.commands",
                    Body = ReadOnlyMemory<byte>.Empty
                },
                cancellationSource.Token);

            await act.Should().ThrowAsync<OperationCanceledException>().ConfigureAwait(false);
            registry.Destination.Should().BeNull();
        }
    }

    /// <summary>
    ///     Verifies a failure before broker publication releases the admitted circuit breaker permit.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task PublishAsync_WhenChannelCreationFails_ShouldReleaseCircuitPermit()
    {
        var registry = new TrackingCircuitBreakerRegistry();
        var publisher = new AmqpPublisher(new UnexpectedConnectionManager(), registry);
        await using (publisher.ConfigureAwait(false))
        {
            var act = () => publisher.PublishAsync(new AmqpPublishRequest
            {
                Exchange = "orders.events",
                RoutingKey = "orders.created",
                Body = ReadOnlyMemory<byte>.Empty
            });

            await act.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(false);
            registry.ReleasedPermits.Should().Be(1);
            registry.RecordedFailures.Should().Be(0);
        }
    }

    private sealed class RecordingCircuitBreakerRegistry : ITransportCircuitBreakerRegistry
    {
        private readonly RejectingCircuitBreaker _circuitBreaker = new();

        public bool IsAnyOpen => true;

        public long FailureCount => 0;

        public string? Destination { get; private set; }

        public ITransportCircuitBreaker GetPublisherCircuit(string destination)
        {
            Destination = destination;
            return _circuitBreaker;
        }
    }

    private sealed class RejectingCircuitBreaker : ITransportCircuitBreaker
    {
        public bool IsOpen => true;

        public int FailureCount => 0;

        public TransportCircuitBreakerPermit AcquirePermit()
        {
            throw new TransportCircuitBreakerOpenException();
        }

        public void RecordSuccess(TransportCircuitBreakerPermit permit)
        {
            throw new InvalidOperationException("A rejected publish cannot record success.");
        }

        public void RecordFailure(TransportCircuitBreakerPermit permit)
        {
            throw new InvalidOperationException("A rejected publish cannot record failure.");
        }

        public void ReleasePermit(TransportCircuitBreakerPermit permit)
        {
            throw new InvalidOperationException("A rejected publish cannot release a permit.");
        }
    }

    private sealed class TrackingCircuitBreakerRegistry :
        ITransportCircuitBreakerRegistry,
        ITransportCircuitBreaker
    {
        public bool IsAnyOpen => false;

        public long FailureCount => RecordedFailures;

        public bool IsOpen => false;

        int ITransportCircuitBreaker.FailureCount => RecordedFailures;

        public int RecordedFailures { get; private set; }

        public int ReleasedPermits { get; private set; }

        public ITransportCircuitBreaker GetPublisherCircuit(string destination)
        {
            return this;
        }

        public TransportCircuitBreakerPermit AcquirePermit()
        {
            return default;
        }

        public void RecordSuccess(TransportCircuitBreakerPermit permit)
        {
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

    private sealed class UnexpectedConnectionManager : IAmqpConnectionManager
    {
        public Task<IConnection> GetConnectionAsync(CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("The publisher must not connect after circuit rejection.");
        }

        public Task<IChannel> CreateChannelAsync(CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("The publisher must not create a channel after circuit rejection.");
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }
}
