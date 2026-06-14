using LiteBus.Transport.IntegrationTesting;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions.Processing;
using LiteBus.Outbox;
using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.Dispatch.InMemory;
using LiteBus.Outbox.Storage.InMemory;
using LiteBus.Testing;
using LiteBus.Transport.Abstractions;
using LiteBus.Transport.InMemory;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Durable.IntegrationTests.Dispatch.Outbox.InMemory;

/// <summary>
///     Verifies outbox at-least-once publication when terminal persist fails after a successful transport publish.
/// </summary>
[Trait("Category", TransportTestTraits.Fast)]
public sealed class InMemoryOutboxAtLeastOnceIntegrationTests : LiteBusTestBase
{
    /// <summary>
    ///     Verifies that a simulated crash after publish causes a second broker publication on lease reclaim.
    /// </summary>
    /// <returns>A task that completes when duplicate publication is observed.</returns>
    [Fact]
    public async Task ProcessPendingAsync_WhenPersistSkippedAfterPublish_ShouldRepublishOnRetry()
    {
        var destination = CreateDestination("at-least-once");
        var clock = new ManualTimeProvider(DateTimeOffset.UtcNow);
        var innerStore = new InMemoryOutboxStore(timeProvider: clock);
        var store = new SkippingPublishedPersistOutboxStore(innerStore);
        var publishCount = 0;
        var firstPublish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var received = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

         var provider = BuildProvider(destination);
         await using (provider.ConfigureAwait(false))
         {
        var broker = provider.GetRequiredService<InMemoryTransportBroker>();
         var consumer = await StartCountingConsumerAsync(
             broker,
             destination,
             () =>
             {
                 if (Interlocked.Increment(ref publishCount) == 1)
                 {
                     firstPublish.TrySetResult();
                 }
             },
             2,
             received).ConfigureAwait(true);
         await using (consumer.ConfigureAwait(true))
         {

        var dispatcher = provider.GetRequiredService<IOutboxDispatcher>();
        var processor = new PipelinedOutboxProcessor(
            store,
            store,
            dispatcher,
            new OutboxProcessorOptions
            {
                BatchSize = 1,
                LeaseOwner = "at-least-once-publisher",
                LeaseDuration = TimeSpan.FromSeconds(5),
                LeaseHeartbeatInterval = TimeSpan.Zero,
                Retry = new RetryOptions { UseJitter = false }
            },
            clock,
            []);

        var messageId = Guid.NewGuid();

        await innerStore.AddAsync(new OutboxEnvelope
        {
            Id = messageId,
            ContractName = "orders.order-submitted",
            ContractVersion = 1,
            Payload = "{\"orderId\":\"11111111-1111-1111-1111-111111111111\"}",
            CreatedAt = clock.GetUtcNow(),
            AttemptCount = 0,
            Status = OutboxStatus.Pending,
            Topic = destination
        }).ConfigureAwait(false);

        await processor.ProcessPendingAsync().ConfigureAwait(false);

        using (var firstPublishCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
        {
            await firstPublish.Task.WaitAsync(firstPublishCancellation.Token).ConfigureAwait(false);
        }

        publishCount.Should().Be(1);
        innerStore.Get(messageId).Status.Should().Be(OutboxStatus.Publishing);

        clock.Advance(TimeSpan.FromSeconds(6));
        await processor.ProcessPendingAsync().ConfigureAwait(false);

        using var cancellationSource = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await received.Task.WaitAsync(cancellationSource.Token).ConfigureAwait(false);

        publishCount.Should().Be(2);
        innerStore.Get(messageId).Status.Should().Be(OutboxStatus.Published);
        innerStore.Get(messageId).AttemptCount.Should().Be(2);
        }
        }
    }

    /// <summary>
    ///     Builds the LiteBus service provider used to resolve transport dispatch dependencies.
    /// </summary>
    /// <param name="destination">The in-memory destination passed to the dispatcher options.</param>
    /// <returns>The configured service provider.</returns>
    private static ServiceProvider BuildProvider(string destination)
    {
        return new ServiceCollection()
            .AddLiteBus(registry =>
            {
                registry.AddMessageModule(_ =>
                {
                });

                registry.AddOutboxModule(builder =>
                {
                    builder.UseInMemoryStorage();
                    builder.Contracts.Register<OrderSubmittedIntegrationEvent>("orders.order-submitted");
                    builder.UseInMemoryDispatch(transport => transport.DefaultDestination = destination);
                });
            })
            .BuildServiceProvider();
    }

    /// <summary>
    ///     Creates a unique destination name for the current test run.
    /// </summary>
    /// <param name="prefix">The prefix identifying the scenario under test.</param>
    /// <returns>A destination name safe for in-memory transport routing.</returns>
    private static string CreateDestination(string prefix)
    {
        return $"litebus-inmemory-{prefix}-{Guid.NewGuid():N}";
    }

    /// <summary>
    ///     Starts a consumer that counts deliveries until the expected total is reached.
    /// </summary>
    /// <param name="broker">The shared in-memory broker backing the consumer.</param>
    /// <param name="destination">The destination name to subscribe to.</param>
    /// <param name="onReceived">The callback invoked for each received message.</param>
    /// <param name="expectedCount">The delivery count that completes the wait task.</param>
    /// <param name="completed">The task source completed when the expected count is reached.</param>
    /// <returns>The started consumer that the caller must stop and dispose.</returns>
    private static async Task<InMemoryConsumer> StartCountingConsumerAsync(
        InMemoryTransportBroker broker,
        string destination,
        Action onReceived,
        int expectedCount,
        TaskCompletionSource completed)
    {
        var remaining = expectedCount;
        var consumer = new InMemoryConsumer(broker);

        await consumer.StartAsync(
            new TransportConsumerOptions { Destination = destination },
            async (message, cancellationToken) =>
            {
                onReceived();

                if (Interlocked.Decrement(ref remaining) == 0)
                {
                    completed.TrySetResult();
                }

                await message.AcceptAsync(cancellationToken).ConfigureAwait(false);
            }).ConfigureAwait(false);

        return consumer;
    }

    /// <summary>
    ///     Skips the first terminal persist attempt for published envelopes to simulate a crash after publish.
    /// </summary>
    private sealed class SkippingPublishedPersistOutboxStore : IOutboxLeaseStore, IOutboxStateWriter
    {
        /// <summary>
        ///     The number of published persist attempts observed by this wrapper.
        /// </summary>
        private int _publishedPersistAttempts;

        /// <summary>
        ///     Initializes a new instance of the <see cref="SkippingPublishedPersistOutboxStore" /> class.
        /// </summary>
        /// <param name="inner">The underlying in-memory store.</param>
        public SkippingPublishedPersistOutboxStore(InMemoryOutboxStore inner)
        {
            Inner = inner;
        }

        /// <summary>
        ///     Gets the underlying in-memory store.
        /// </summary>
        public InMemoryOutboxStore Inner { get; }

        /// <inheritdoc />
        public Task<IReadOnlyList<OutboxEnvelope>> LeasePendingAsync(
            OutboxLeaseRequest request,
            CancellationToken cancellationToken = default)
        {
            return Inner.LeasePendingAsync(request, cancellationToken);
        }

        /// <inheritdoc />
        public Task<bool> RenewLeaseAsync(
            LeaseRenewalRequest request,
            CancellationToken cancellationToken = default)
        {
            return Inner.RenewLeaseAsync(request, cancellationToken);
        }

        /// <inheritdoc />
        public Task<PersistResult> PersistAsync(
            IReadOnlyList<OutboxEnvelope> envelopes,
            CancellationToken cancellationToken = default)
        {
            if (envelopes.Any(envelope => envelope.Status == OutboxStatus.Published) &&
                Interlocked.Increment(ref _publishedPersistAttempts) == 1)
            {
                return Task.FromResult(PersistResult.FromOutcome(0, envelopes.Count));
            }

            return Inner.PersistAsync(envelopes, cancellationToken);
        }
    }
}
