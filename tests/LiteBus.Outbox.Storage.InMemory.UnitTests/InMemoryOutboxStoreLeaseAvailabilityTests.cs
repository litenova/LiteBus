using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.Storage.InMemory;

namespace LiteBus.Outbox.Storage.InMemory.UnitTests;

public sealed class InMemoryOutboxStoreLeaseAvailabilityTests
{
    [Fact]
    public async Task LeasePendingAsync_WhenPublishingWithNullLeaseExpiry_ShouldNotLease()
    {
        var store = new InMemoryOutboxStore();
        var now = DateTimeOffset.UtcNow;
        var envelope = new OutboxEnvelope
        {
            Id = Guid.NewGuid(),
            ContractName = "test.event",
            ContractVersion = 1,
            Payload = "{}",
            CreatedAt = now,
            AttemptCount = 0,
            Status = OutboxStatus.Publishing,
            LeaseExpiresAt = null
        };

        await store.AddAsync(envelope);

        var leased = await store.LeasePendingAsync(
            new OutboxLeaseRequest
            {
                Now = now,
                LeaseDuration = TimeSpan.FromMinutes(1),
                BatchSize = 10,
                LeaseOwner = "worker"
            });

        leased.Should().BeEmpty();
    }
}
