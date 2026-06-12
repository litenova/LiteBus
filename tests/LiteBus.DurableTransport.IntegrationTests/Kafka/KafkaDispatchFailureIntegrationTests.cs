using LiteBus.DurableTransport.IntegrationTesting;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions.DurableMessaging;
using LiteBus.Outbox;
using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.Dispatch.Kafka;
using LiteBus.Outbox.Storage.InMemory;
using LiteBus.Testing;
using LiteBus.Transport;
using LiteBus.Transport.Kafka;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.DurableTransport.IntegrationTests.Kafka;

/// <summary>
///     Verifies outbox dispatch failure handling through the real Kafka transport dispatcher.
/// </summary>
public sealed class KafkaDispatchFailureIntegrationTests : LiteBusTestBase
{
    /// <summary>
    ///     Verifies unreachable bootstrap servers leave outbox rows in a failed state with a retry schedule.
    /// </summary>
    /// <returns>A task that completes when the failure assertion succeeds.</returns>
    [Fact]
    public async Task ProcessPendingAsync_WhenBrokerUnreachable_ShouldMarkFailedWithVisibleAfter()
    {
        var messageId = Guid.NewGuid();
        var clock = new ManualTimeProvider(DateTimeOffset.UtcNow);

        var provider = BuildProvider(
            new KafkaTransportOptions
            {
                BootstrapServers = "127.0.0.1:1",
                ConsumerGroupId = $"litebus-fail-{Guid.NewGuid():N}",
                MessageTimeoutMs = 3_000
            },
            clock);

        try
        {
            var outbox = provider.GetRequiredService<IOutbox>();
            var processor = provider.GetRequiredService<IOutboxProcessor>();
            var store = provider.GetRequiredService<InMemoryOutboxStore>();

            await outbox.EnqueueAsync(new OutboxEnqueueItem<OrderSubmittedIntegrationEvent>
            {
                Message = new OrderSubmittedIntegrationEvent { OrderId = Guid.NewGuid() },
                Metadata = OutboxEnqueueMetadata.Immediate with
                {
                    Identity = new MessageIdentity.Supplied(messageId)
                }
            }).ConfigureAwait(false);

            await processor.ProcessPendingAsync().ConfigureAwait(false);

            var row = store.Get(messageId);
            row.Status.Should().Be(OutboxStatus.Failed);
            row.VisibleAfter.Should().BeAfter(clock.GetUtcNow());
        }
        finally
        {
            await KafkaTransportTestInfrastructure.DisposeProviderSafelyAsync(provider).ConfigureAwait(false);
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
            new KafkaTransportOptions
            {
                BootstrapServers = "127.0.0.1:1",
                ConsumerGroupId = $"litebus-cb-{Guid.NewGuid():N}",
                MessageTimeoutMs = 3_000
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
                Message = new OrderSubmittedIntegrationEvent { OrderId = Guid.NewGuid() },
                Metadata = OutboxEnqueueMetadata.Immediate with
                {
                    Identity = new MessageIdentity.Supplied(messageId)
                }
            }).ConfigureAwait(false);

            await processor.ProcessPendingAsync().ConfigureAwait(false);

            var row = store.Get(messageId);
            row.Status.Should().Be(OutboxStatus.Failed);
            row.Status.Should().NotBe(OutboxStatus.Published);
        }
        finally
        {
            await KafkaTransportTestInfrastructure.DisposeProviderSafelyAsync(provider).ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Builds a LiteBus service provider configured for Kafka dispatch failure tests.
    /// </summary>
    /// <param name="transportOptions">The Kafka connection settings under test.</param>
    /// <param name="clock">The optional clock used by the outbox store.</param>
    /// <returns>The configured service provider.</returns>
    private static ServiceProvider BuildProvider(
        KafkaTransportOptions transportOptions,
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
                    LeaseOwner = "kafka-dispatch-failure",
                    Retry = new RetryOptions
                    {
                        UseJitter = false,
                        InitialDelay = TimeSpan.FromMinutes(2),
                        MaxAttempts = 5
                    }
                });

                outbox.UseKafkaDispatch(
                    transport => transport.DefaultDestination = "unreachable-topic",
                    transportOptions);
            });
        });

        return services.BuildServiceProvider();
    }
}