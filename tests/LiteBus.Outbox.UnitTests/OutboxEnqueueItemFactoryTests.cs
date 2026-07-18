using LiteBus.Messaging.Abstractions.DurableMessaging;
using LiteBus.Outbox.Abstractions;

namespace LiteBus.Outbox.UnitTests;

/// <summary>
///     Verifies typed outbox enqueue-item factories compose the intended durable metadata.
/// </summary>
public sealed class OutboxEnqueueItemFactoryTests
{
    /// <summary>
    ///     Verifies scheduling, identity, idempotency, topic, and explicit metadata factory variants.
    /// </summary>
    [Fact]
    public void Factories_ShouldComposeDurableMetadata()
    {
        var message = new TestEvent();
        var messageId = Guid.NewGuid();
        var visibleAfter = DateTimeOffset.UtcNow.AddHours(1);
        var delay = TimeSpan.FromMinutes(5);
        var explicitMetadata = OutboxEnqueueMetadata.Immediate with
        {
            Tenant = new TenantScope.Isolated("tenant-a")
        };

        var scheduledAt = OutboxEnqueueItem<TestEvent>.ScheduledAt(message, visibleAfter);
        var scheduledAfter = OutboxEnqueueItem<TestEvent>.ScheduledAfter(message, delay);
        var idempotent = OutboxEnqueueItem<TestEvent>.WithIdempotency(message, "event-key");
        var identified = OutboxEnqueueItem<TestEvent>.WithIdentity(message, messageId);
        var targeted = OutboxEnqueueItem<TestEvent>.WithTopic(message, "orders");
        var explicitItem = OutboxEnqueueItem<TestEvent>.From(message, explicitMetadata);

        scheduledAt.Metadata.Visibility.Should().Be(new MessageVisibility.At(visibleAfter));
        scheduledAfter.Metadata.Visibility.Should().Be(new MessageVisibility.After(delay));
        idempotent.Metadata.Idempotency.Should().Be(new Idempotency.Keyed("event-key"));
        identified.Metadata.Identity.Should().Be(new MessageIdentity.Supplied(messageId));
        targeted.Metadata.Target.Should().Be(new PublicationTarget.Topic("orders"));
        explicitItem.Metadata.Should().BeSameAs(explicitMetadata);
        explicitItem.Message.Should().BeSameAs(message);
    }

    private sealed record TestEvent;
}
