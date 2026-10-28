using LiteBus.Commands;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Dispatch.InProcess;
using LiteBus.Inbox.Storage.InMemory;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Inbox.UnitTests.Testing;

/// <summary>
///     Verifies the in-memory transactional inbox registered by <see cref="InboxOutboxStoreServiceCollectionExtensions.AddTestTransactionalInbox(IServiceCollection, TestInboxStore)" />.
/// </summary>
[Collection("Sequential")]
public sealed class TestTransactionalInboxTests : LiteBusTestBase
{
    /// <summary>
    ///     Verifies that the test inbox store carries the transactional writer marker.
    /// </summary>
    [Fact]
    public void TestInboxStore_should_implement_the_transactional_store_marker()
    {
        new TestInboxStore().Should().BeAssignableTo<ITransactionalInboxStore>();
    }

    /// <summary>
    ///     Verifies that a transactional accept stages a pending envelope that a drain later completes.
    /// </summary>
    [Fact]
    public async Task AcceptAsync_should_stage_envelope_then_complete_on_drain()
    {
        var store = new TestInboxStore();
        var recorder = new InboxTestFixtures.CommandRecorder();
        var provider = CreateProvider(store, recorder);

        using var scope = provider.CreateScope();
        var transactional = scope.ServiceProvider.GetRequiredService<ITransactionalInbox>();

        var orderId = Guid.NewGuid();
        var receipt = await transactional.AcceptAsync(new InboxTestFixtures.ShipOrderCommand
        {
            OrderId = orderId,
            IdempotencyKey = $"ship:{orderId}"
        }).ConfigureAwait(false);

        receipt.Contract.Name.Should().Be("orders.commands.ship");
        store.Get(receipt.Id).Status.Should().Be(InboxStatus.Pending);

        var drain = await ProcessorPassHelpers.DrainInboxAsync(provider).ConfigureAwait(false);

        drain.SucceededCount.Should().Be(1);
        store.Get(receipt.Id).Status.Should().Be(InboxStatus.Completed);
        recorder.Commands.Should().ContainSingle(command => command.OrderId == orderId);
    }

    /// <summary>
    ///     Verifies that a transactional batch stages every envelope in order and completes all of them.
    /// </summary>
    [Fact]
    public async Task AcceptBatchAsync_should_stage_batch_in_order_then_complete_all()
    {
        var store = new TestInboxStore();
        var recorder = new InboxTestFixtures.CommandRecorder();
        var provider = CreateProvider(store, recorder);

        using var scope = provider.CreateScope();
        var transactional = scope.ServiceProvider.GetRequiredService<ITransactionalInbox>();

        var firstOrderId = Guid.NewGuid();
        var secondOrderId = Guid.NewGuid();
        var receipts = await transactional.AcceptBatchAsync(
        [
            InboxAcceptItem.From(new InboxTestFixtures.ShipOrderCommand
            {
                OrderId = firstOrderId,
                IdempotencyKey = $"ship:{firstOrderId}"
            }),
            InboxAcceptItem.From(new InboxTestFixtures.ShipOrderCommand
            {
                OrderId = secondOrderId,
                IdempotencyKey = $"ship:{secondOrderId}"
            })
        ]).ConfigureAwait(false);

        receipts.Select(receipt => receipt.Contract.Name).Should().Equal("orders.commands.ship", "orders.commands.ship");
        receipts.Select(receipt => store.Get(receipt.Id).Status).Should().OnlyContain(status => status == InboxStatus.Pending);

        var drain = await ProcessorPassHelpers.DrainInboxAsync(provider).ConfigureAwait(false);

        drain.SucceededCount.Should().Be(2);
        store.Get(receipts[0].Id).Status.Should().Be(InboxStatus.Completed);
        store.Get(receipts[1].Id).Status.Should().Be(InboxStatus.Completed);
        recorder.Commands.Should().HaveCount(2);
    }

    /// <summary>
    ///     Verifies that the parameterless helper registers a shared store behind every inbox role.
    /// </summary>
    [Fact]
    public async Task AddTestTransactionalInbox_should_register_a_shared_store_for_every_role()
    {
        var recorder = new InboxTestFixtures.CommandRecorder();
        var provider = CreateProvider(store: null, recorder);

        using var scope = provider.CreateScope();
        var transactional = scope.ServiceProvider.GetRequiredService<ITransactionalInbox>();
        var store = provider.GetRequiredService<TestInboxStore>();

        provider.GetRequiredService<ITransactionalInboxStore>().Should().BeSameAs(store);

        var orderId = Guid.NewGuid();
        var receipt = await transactional.AcceptAsync(new InboxTestFixtures.ShipOrderCommand
        {
            OrderId = orderId,
            IdempotencyKey = $"ship:{orderId}"
        }).ConfigureAwait(false);

        store.Get(receipt.Id).Status.Should().Be(InboxStatus.Pending);
    }

    /// <summary>
    ///     Builds a provider with in-memory storage, in-process dispatch, and the test transactional inbox.
    /// </summary>
    /// <param name="store">The shared store instance used by every inbox role, or <see langword="null" /> to let the helper create one.</param>
    /// <param name="recorder">The recorder that observes dispatched commands.</param>
    /// <returns>The configured service provider.</returns>
    private static ServiceProvider CreateProvider(TestInboxStore? store, InboxTestFixtures.CommandRecorder recorder)
    {
        var services = new ServiceCollection()
            .AddSingleton(recorder)
            .AddLiteBus(builder =>
            {
                builder.AddMessaging(_ =>
                {
                });

                builder.AddCommands(commands =>
                {
                    commands.Register<InboxTestFixtures.ShipOrderCommand>();
                    commands.Register<InboxTestFixtures.ShipOrderCommandHandler>();
                });

                builder.AddInbox(inbox =>
                {
                    inbox.Contracts.Register<InboxTestFixtures.ShipOrderCommand>("orders.commands.ship");

                    inbox.UseProcessorOptions(new InboxProcessorOptions
                    {
                        BatchSize = 10,
                        LeaseOwner = "test-worker",
                        Retry = new RetryOptions { UseJitter = false }
                    });

                    inbox.UseInMemoryStorage();
                    inbox.UseInProcessDispatch();
                });
            });

        if (store is null)
        {
            services.AddTestTransactionalInbox();
        }
        else
        {
            services.AddTestTransactionalInbox(store);
        }

        return services.BuildServiceProvider();
    }
}
