using LiteBus.Outbox.Abstractions;

namespace LiteBus.Outbox.Storage.InMemory.UnitTests;

/// <summary>
///     Verifies the in-memory outbox idempotency index stays consistent after retention and clear operations.
/// </summary>
public sealed class InMemoryOutboxIdempotencyIndexTests
{
    /// <summary>
    ///     Confirms a duplicate idempotency key can be enqueued again after published rows are deleted.
    /// </summary>
    [Fact]
    public async Task DeletePublishedOlderThanAsync_ShouldRemoveIdempotencyIndexEntry()
    {
        var store = new InMemoryOutboxStore();
        var now = new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);
        const string idempotencyKey = "order-42";

        var firstId = Guid.NewGuid();

        await store.EnqueueAsync(new OutboxEnvelope
        {
            Id = firstId,
            ContractName = "tests.events.submitted",
            ContractVersion = 1,
            Payload = """{"n":1}""",
            CreatedAt = now,
            Status = OutboxStatus.Pending,
            AttemptCount = 0,
            IdempotencyKey = idempotencyKey
        });

        var leased = await store.LeasePendingAsync(new OutboxLeaseRequest
        {
            BatchSize = 1,
            LeaseOwner = "test",
            Now = now.AddSeconds(1),
            LeaseDuration = TimeSpan.FromMinutes(1)
        });

        await store.PersistAsync([leased[0].AsPublished() with { PublishedAt = now.AddHours(-2) }]);

        var deleted = await store.DeletePublishedOlderThanAsync(now.AddHours(1));
        deleted.Should().Be(1);

        var secondId = Guid.NewGuid();

        var stored = await store.EnqueueAsync(new OutboxEnvelope
        {
            Id = secondId,
            ContractName = "tests.events.submitted",
            ContractVersion = 1,
            Payload = """{"n":2}""",
            CreatedAt = now.AddMinutes(1),
            Status = OutboxStatus.Pending,
            AttemptCount = 0,
            IdempotencyKey = idempotencyKey
        });

        stored.Id.Should().Be(secondId);
    }

    /// <summary>
    ///     Confirms <see cref="InMemoryOutboxStore.Clear" /> resets the idempotency index.
    /// </summary>
    [Fact]
    public async Task Clear_ShouldRemoveIdempotencyIndexEntry()
    {
        var store = new InMemoryOutboxStore();
        var now = DateTimeOffset.UtcNow;
        const string idempotencyKey = "order-clear";

        await store.EnqueueAsync(new OutboxEnvelope
        {
            Id = Guid.NewGuid(),
            ContractName = "tests.events.submitted",
            ContractVersion = 1,
            Payload = """{"n":1}""",
            CreatedAt = now,
            Status = OutboxStatus.Pending,
            AttemptCount = 0,
            IdempotencyKey = idempotencyKey
        });

        store.Clear();

        var secondId = Guid.NewGuid();

        var stored = await store.EnqueueAsync(new OutboxEnvelope
        {
            Id = secondId,
            ContractName = "tests.events.submitted",
            ContractVersion = 1,
            Payload = """{"n":2}""",
            CreatedAt = now,
            Status = OutboxStatus.Pending,
            AttemptCount = 0,
            IdempotencyKey = idempotencyKey
        });

        stored.Id.Should().Be(secondId);
    }
}