using LiteBus.DurableTransport.IntegrationTesting;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions.DurableMessaging;
using LiteBus.Outbox;
using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.Dispatch.Aws;
using LiteBus.Outbox.Storage.InMemory;
using LiteBus.Testing;
using LiteBus.Transport;
using LiteBus.Transport.Aws;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.DurableTransport.IntegrationTests.Aws;

/// <summary>
///     Verifies outbox dispatch failure handling through the real AWS SQS transport dispatcher.
/// </summary>
[Trait("Category", TransportTestTraits.Docker)]
public sealed class AwsSqsDispatchFailureIntegrationTests : LiteBusTestBase
{
    /// <summary>
    ///     Verifies unreachable service endpoints leave outbox rows in a failed state with a retry schedule.
    /// </summary>
    /// <returns>A task that completes when the failure assertion succeeds.</returns>
    [Fact]
    public async Task ProcessPendingAsync_WhenBrokerUnreachable_ShouldMarkFailedWithVisibleAfter()
    {
        var messageId = Guid.NewGuid();
        var clock = new ManualTimeProvider(DateTimeOffset.UtcNow);

        var provider = BuildProvider(
            new AwsSqsTransportOptions
            {
                ServiceUrl = "http://127.0.0.1:1",
                Region = "us-east-1",
                AccessKey = "test",
                SecretKey = "test"
            },
            clock);

        try
        {
            var outbox = provider.GetRequiredService<IOutbox>();
            var processor = provider.GetRequiredService<IOutboxProcessor>();
            var store = provider.GetRequiredService<InMemoryOutboxStore>();

            await outbox.EnqueueAsync(new OutboxEnqueueItem<OrderSubmittedIntegrationEvent>
            {
                Event = new OrderSubmittedIntegrationEvent { OrderId = Guid.NewGuid() },
                Metadata = OutboxEnqueueMetadata.Immediate with
                {
                    Identity = new MessageIdentity.Supplied(messageId)
                }
            });

            await processor.ProcessPendingAsync();

            var row = store.Get(messageId);
            row.Status.Should().Be(OutboxStatus.Failed);
            row.VisibleAfter.Should().BeAfter(clock.GetUtcNow());
        }
        finally
        {
            await provider.DisposeAsync();
        }
    }

    /// <summary>
    ///     Verifies an open transport circuit breaker prevents publishing and records a retryable failure.
    /// </summary>
    /// <returns>A task that completes when the circuit breaker assertion succeeds.</returns>
    [Fact]
    public async Task ProcessPendingAsync_WhenCircuitBreakerOpen_ShouldNotPublish()
    {
        var messageId = Guid.NewGuid();

        var provider = BuildProvider(
            new AwsSqsTransportOptions
            {
                ServiceUrl = "http://127.0.0.1:1",
                Region = "us-east-1",
                AccessKey = "test",
                SecretKey = "test"
            });

        try
        {
            var breaker = provider.GetRequiredService<ITransportCircuitBreaker>();

            for (var attempt = 0; attempt < 5; attempt++)
            {
                breaker.RecordFailure();
            }

            var outbox = provider.GetRequiredService<IOutbox>();
            var processor = provider.GetRequiredService<IOutboxProcessor>();
            var store = provider.GetRequiredService<InMemoryOutboxStore>();

            await outbox.EnqueueAsync(new OutboxEnqueueItem<OrderSubmittedIntegrationEvent>
            {
                Event = new OrderSubmittedIntegrationEvent { OrderId = Guid.NewGuid() },
                Metadata = OutboxEnqueueMetadata.Immediate with
                {
                    Identity = new MessageIdentity.Supplied(messageId)
                }
            });

            await processor.ProcessPendingAsync();

            var row = store.Get(messageId);
            row.Status.Should().Be(OutboxStatus.Failed);
            row.Status.Should().NotBe(OutboxStatus.Published);
        }
        finally
        {
            await provider.DisposeAsync();
        }
    }

    /// <summary>
    ///     Builds a LiteBus service provider configured for SQS dispatch failure tests.
    /// </summary>
    /// <param name="transportOptions">The SQS connection settings under test.</param>
    /// <param name="clock">The optional clock used by the outbox store.</param>
    /// <param name="circuitBreaker">The optional circuit breaker registered for transport publishing.</param>
    /// <returns>The configured service provider.</returns>
    private static ServiceProvider BuildProvider(
        AwsSqsTransportOptions transportOptions,
        TimeProvider? clock = null)
    {
        var services = new ServiceCollection();

        if (clock is not null)
        {
            services.AddSingleton(clock);
        }

        services.AddLiteBus(registry =>
        {
            registry.AddMessageModule(_ =>
            {
            });

            registry.AddOutboxModule(outbox =>
            {
                outbox.UseInMemoryStorage();
                outbox.Contracts.Register<OrderSubmittedIntegrationEvent>("orders.order-submitted");

                outbox.UseProcessorOptions(new OutboxProcessorOptions
                {
                    BatchSize = 10,
                    LeaseOwner = "sqs-dispatch-failure",
                    Retry = new RetryOptions
                    {
                        UseJitter = false,
                        InitialDelay = TimeSpan.FromMinutes(2),
                        MaxAttempts = 5
                    }
                });

                outbox.UseAwsSqsDispatch(
                    transport => transport.DefaultDestination = "http://127.0.0.1:1/000000000000/unreachable",
                    transportOptions);
            });
        });

        return services.BuildServiceProvider();
    }
}