using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Storage.InMemory;
using LiteBus.Messaging;
using LiteBus.Testing;

namespace LiteBus.Inbox.UnitTests;

/// <summary>
///     Verifies <see cref="MessageVisibility" /> deferred acceptance semantics through <see cref="IInbox" />.
/// </summary>
public sealed class MessageVisibilityTests
{
    /// <summary>
    ///     Verifies <see cref="MessageVisibility.At" /> stores a future visible-after timestamp.
    /// </summary>
    [Fact]
    public async Task AcceptAsync_with_At_visibility_should_persist_visible_after()
    {
        var now = new DateTimeOffset(2026, 6, 6, 9, 0, 0, TimeSpan.Zero);
        var visibleAfter = now.AddHours(2);
        var store = new InMemoryInboxStore();
        var contractRegistry = new MessageContractRegistry();
        contractRegistry.Register<InboxTestFixtures.ShipOrderCommand>("orders.commands.ship");

        var inbox = InboxWriterTestFactory.Create(
            store,
            contractRegistry,
            new SystemTextJsonMessageSerializer(),
            new ManualTimeProvider(now));

        var orderId = Guid.NewGuid();

        var receipt = await inbox.AcceptAsync(InboxWriterTestFactory.Item(
            new InboxTestFixtures.ShipOrderCommand
            {
                OrderId = orderId,
                IdempotencyKey = $"ship:{orderId}"
            },
            InboxAcceptMetadata.Immediate with
            {
                Visibility = new MessageVisibility.At(visibleAfter)
            }));

        receipt.AcceptedAt.Should().Be(now);
        store.Get(receipt.Id).VisibleAfter.Should().Be(visibleAfter);
    }

    /// <summary>
    ///     Verifies <see cref="MessageVisibility.After" /> applies a relative delay at accept time.
    /// </summary>
    [Fact]
    public async Task AcceptAsync_with_After_visibility_should_apply_relative_delay()
    {
        var now = new DateTimeOffset(2026, 6, 6, 9, 0, 0, TimeSpan.Zero);
        var delay = TimeSpan.FromMinutes(30);
        var store = new InMemoryInboxStore();
        var contractRegistry = new MessageContractRegistry();
        contractRegistry.Register<InboxTestFixtures.ShipOrderCommand>("orders.commands.ship");

        var inbox = InboxWriterTestFactory.Create(
            store,
            contractRegistry,
            new SystemTextJsonMessageSerializer(),
            new ManualTimeProvider(now));

        var orderId = Guid.NewGuid();

        var receipt = await inbox.AcceptAsync(InboxWriterTestFactory.Item(
            new InboxTestFixtures.ShipOrderCommand
            {
                OrderId = orderId,
                IdempotencyKey = $"ship:{orderId}"
            },
            InboxAcceptMetadata.Immediate with
            {
                Visibility = new MessageVisibility.After(delay)
            }));

        store.Get(receipt.Id).VisibleAfter.Should().Be(now.Add(delay));
    }
}