using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.Storage.InMemory;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Outbox.UnitTests.Testing;

/// <summary>
///     Verifies the in-memory transactional outbox registered by <see cref="InboxOutboxStoreServiceCollectionExtensions.AddTestTransactionalOutbox(IServiceCollection, TestOutboxStore)" />.
/// </summary>
[Collection("Sequential")]
public sealed class TestTransactionalOutboxTests : LiteBusTestBase
{
    /// <summary>
    ///     Verifies that a transactional enqueue stages a pending envelope that a drain later publishes.
    /// </summary>
    [Fact]
    public async Task EnqueueAsync_should_stage_envelope_then_publish_on_drain()
    {
        var store = new TestOutboxStore();
        var dispatcher = new ScriptedOutboxDispatcher();
        var provider = CreateProvider(store, dispatcher);

        using var scope = provider.CreateScope();
        var transactional = scope.ServiceProvider.GetRequiredService<ITransactionalOutbox>();

        var orderId = Guid.NewGuid();
        var receipt = await transactional.EnqueueAsync(
            OutboxEnqueueItem<OrderPlaced>.From(new OrderPlaced { OrderId = orderId })).ConfigureAwait(false);

        receipt.Contract.Name.Should().Be("orders.placed");
        store.Get(receipt.Id).Status.Should().Be(OutboxStatus.Pending);

        var drain = await ProcessorPassHelpers.DrainOutboxAsync(provider).ConfigureAwait(false);

        drain.SucceededCount.Should().Be(1);
        store.Get(receipt.Id).Status.Should().Be(OutboxStatus.Published);
        dispatcher.DispatchedEnvelopes.Should().ContainSingle(envelope => envelope.Id == receipt.Id);
    }

    /// <summary>
    ///     Verifies that a heterogeneous transactional batch stages every envelope in order and publishes all of them.
    /// </summary>
    [Fact]
    public async Task EnqueueBatchAsync_should_stage_batch_in_order_then_publish_all()
    {
        var store = new TestOutboxStore();
        var dispatcher = new ScriptedOutboxDispatcher();
        var provider = CreateProvider(store, dispatcher);

        using var scope = provider.CreateScope();
        var transactional = scope.ServiceProvider.GetRequiredService<ITransactionalOutbox>();

        var orderId = Guid.NewGuid();
        var receipts = await transactional.EnqueueBatchAsync(
        [
            OutboxEnqueueItem.From(new OrderPlaced { OrderId = orderId }),
            OutboxEnqueueItem.From(new PaymentTaken { OrderId = orderId })
        ]).ConfigureAwait(false);

        receipts.Select(receipt => receipt.Contract.Name).Should().Equal("orders.placed", "payments.taken");
        receipts.Select(receipt => store.Get(receipt.Id).Status).Should().OnlyContain(status => status == OutboxStatus.Pending);

        var drain = await ProcessorPassHelpers.DrainOutboxAsync(provider).ConfigureAwait(false);

        drain.SucceededCount.Should().Be(2);
        store.Get(receipts[0].Id).Status.Should().Be(OutboxStatus.Published);
        store.Get(receipts[1].Id).Status.Should().Be(OutboxStatus.Published);
        dispatcher.DispatchedEnvelopes.Should().HaveCount(2);
    }

    /// <summary>
    ///     Builds a provider with in-memory storage, a scripted dispatcher, and the test transactional outbox.
    /// </summary>
    /// <param name="store">The shared store instance used by every outbox role.</param>
    /// <param name="dispatcher">The fixed dispatcher instance used for every dispatch call.</param>
    /// <returns>The configured service provider.</returns>
    private static ServiceProvider CreateProvider(TestOutboxStore store, ScriptedOutboxDispatcher dispatcher)
    {
        return new ServiceCollection()
            .AddLiteBus(builder =>
            {
                builder.AddMessaging(_ =>
                {
                });

                builder.AddOutbox(outbox =>
                {
                    outbox.Contracts.Register<OrderPlaced>("orders.placed");
                    outbox.Contracts.Register<PaymentTaken>("payments.taken");

                    outbox.UseProcessorOptions(new OutboxProcessorOptions
                    {
                        BatchSize = 10,
                        LeaseOwner = "test-publisher",
                        Retry = new RetryOptions { UseJitter = false }
                    });

                    outbox.UseInMemoryStorage();
                    outbox.UseFixedOutboxDispatcher(dispatcher);
                });
            })
            .AddTestTransactionalOutbox(store)
            .BuildServiceProvider();
    }

    /// <summary>
    ///     Sample integration event used by tests.
    /// </summary>
    private sealed record OrderPlaced
    {
        /// <summary>
        ///     Gets the order identifier.
        /// </summary>
        public Guid OrderId { get; init; }
    }

    /// <summary>
    ///     Sample integration event used by tests.
    /// </summary>
    private sealed record PaymentTaken
    {
        /// <summary>
        ///     Gets the order identifier.
        /// </summary>
        public Guid OrderId { get; init; }
    }
}
