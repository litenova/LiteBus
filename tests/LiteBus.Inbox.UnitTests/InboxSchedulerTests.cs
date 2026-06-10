using LiteBus.Inbox;
using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Storage.InMemory;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Testing;

namespace LiteBus.Inbox.UnitTests;

/// <summary>
///     Verifies <see cref="IInboxScheduler" /> delayed acceptance semantics.
/// </summary>
public sealed class InboxSchedulerTests
{
    /// <summary>
    ///     Verifies <see cref="IInboxScheduler.ScheduleAsync" /> stores a future visible-after timestamp.
    /// </summary>
    [Fact]
    public async Task ScheduleAsync_ShouldPersistVisibleAfter()
    {
        var now = new DateTimeOffset(2026, 6, 6, 9, 0, 0, TimeSpan.Zero);
        var visibleAfter = now.AddHours(2);
        var store = new InMemoryInboxStore();
        var contractRegistry = new MessageContractRegistry();
        contractRegistry.Register<InboxTestFixtures.ShipOrderCommand>("orders.commands.ship", 1);

        IInboxScheduler scheduler = new Inbox(
            store,
            contractRegistry,
            new SystemTextJsonMessageSerializer(),
            new ManualTimeProvider(now));

        var orderId = Guid.NewGuid();
        var receipt = await scheduler.ScheduleAsync(
            new InboxTestFixtures.ShipOrderCommand
            {
                OrderId = orderId,
                IdempotencyKey = $"ship:{orderId}"
            },
            visibleAfter);

        receipt.AcceptedAt.Should().Be(now);
        store.Get(receipt.Id).VisibleAfter.Should().Be(visibleAfter);
    }

    /// <summary>
    ///     Verifies <see cref="IInboxScheduler.ScheduleAfterAsync" /> applies a relative delay.
    /// </summary>
    [Fact]
    public async Task ScheduleAfterAsync_ShouldApplyRelativeDelay()
    {
        var now = new DateTimeOffset(2026, 6, 6, 9, 0, 0, TimeSpan.Zero);
        var delay = TimeSpan.FromMinutes(30);
        var store = new InMemoryInboxStore();
        var contractRegistry = new MessageContractRegistry();
        contractRegistry.Register<InboxTestFixtures.ShipOrderCommand>("orders.commands.ship", 1);

        IInboxScheduler scheduler = new Inbox(
            store,
            contractRegistry,
            new SystemTextJsonMessageSerializer(),
            new ManualTimeProvider(now));

        var orderId = Guid.NewGuid();
        var receipt = await scheduler.ScheduleAfterAsync(
            new InboxTestFixtures.ShipOrderCommand
            {
                OrderId = orderId,
                IdempotencyKey = $"ship:{orderId}"
            },
            delay);

        store.Get(receipt.Id).VisibleAfter.Should().Be(now.Add(delay));
    }
}
