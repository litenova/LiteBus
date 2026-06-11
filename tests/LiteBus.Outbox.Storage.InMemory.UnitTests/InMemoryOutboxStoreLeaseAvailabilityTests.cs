using LiteBus.Outbox.Abstractions;

namespace LiteBus.Outbox.Storage.InMemory.UnitTests;

/// <summary>
///     Tests stale in-flight recovery for in-memory outbox leasing.
/// </summary>
public sealed class InMemoryOutboxStoreLeaseAvailabilityTests
{
    /// <summary>
    ///     Confirms publishing rows with a null lease expiry are not reclaimed before the stale cutoff.
    /// </summary>
    [Fact]
    public async Task LeasePendingAsync_WhenPublishingWithNullLeaseExpiry_ShouldNotLeaseImmediately()
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

    /// <summary>
    ///     Confirms stale publishing rows with a null lease expiry are reclaimed after the cutoff.
    /// </summary>
    [Fact]
    public async Task LeasePendingAsync_WhenPublishingWithNullLeaseExpiryBecomesStale_ShouldReclaim()
    {
        var store = new InMemoryOutboxStore();
        var now = DateTimeOffset.UtcNow;
        var messageId = Guid.NewGuid();

        await store.AddAsync(new OutboxEnvelope
        {
            Id = messageId,
            ContractName = "test.event",
            ContractVersion = 1,
            Payload = "{}",
            CreatedAt = now.AddMinutes(-5),
            AttemptCount = 1,
            Status = OutboxStatus.Publishing,
            LeaseOwner = "stale-publisher",
            LeaseExpiresAt = null
        });

        var reclaimed = await store.LeasePendingAsync(
            new OutboxLeaseRequest
            {
                Now = now,
                LeaseDuration = TimeSpan.FromMinutes(1),
                BatchSize = 10,
                LeaseOwner = "fresh-publisher"
            });

        reclaimed.Should().ContainSingle();
        reclaimed[0].Id.Should().Be(messageId);
        reclaimed[0].LeaseOwner.Should().Be("fresh-publisher");
        reclaimed[0].AttemptCount.Should().Be(2);
    }
}