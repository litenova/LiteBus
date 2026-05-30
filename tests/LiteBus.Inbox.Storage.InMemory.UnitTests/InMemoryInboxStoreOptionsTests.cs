using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Storage.InMemory;

namespace LiteBus.Inbox.Storage.InMemory.UnitTests;

/// <summary>
///     Tests inbox-specific in-memory store behavior beyond the shared contract suite.
/// </summary>
public sealed class InMemoryInboxStoreOptionsTests
{
    private static readonly DateTimeOffset BaseTime = new(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task AddAsync_WhenCapacityReached_ShouldThrow()
    {
        var store = new InMemoryInboxStore(new InMemoryInboxStoreOptions { Capacity = 1 });
        var now = BaseTime;

        await store.AddAsync(CreatePendingEnvelope(Guid.NewGuid(), now));

        var act = () => store.AddAsync(CreatePendingEnvelope(Guid.NewGuid(), now.AddSeconds(1)));

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*capacity of 1*");
    }

    [Fact]
    public async Task AddAsync_WhenCapacityReachedButSubmissionIsIdempotent_ShouldReturnExistingCommand()
    {
        var store = new InMemoryInboxStore(new InMemoryInboxStoreOptions { Capacity = 1 });
        var now = BaseTime;
        const string idempotencyKey = "ship-capacity";

        var first = await store.AddAsync(new InboxEnvelope
        {
            Id = Guid.NewGuid(),
            ContractName = "tests.commands.ship",
            ContractVersion = 1,
            Payload = "{\"orderId\":\"1\"}",
            CreatedAt = now,
            AttemptCount = 0,
            Status = InboxStatus.Pending,
            IdempotencyKey = idempotencyKey
        });

        var duplicate = await store.AddAsync(first with
        {
            Id = Guid.NewGuid(),
            Payload = "{\"orderId\":\"2\"}"
        });

        duplicate.Id.Should().Be(first.Id);
        store.Count.Should().Be(1);
    }

    [Fact]
    public async Task LeasePendingAsync_WhenLeaseDurationIsZero_ShouldUseDefaultLeaseDuration()
    {
        var store = new InMemoryInboxStore(new InMemoryInboxStoreOptions
        {
            DefaultLeaseDuration = TimeSpan.FromMinutes(5)
        });
        var commandId = Guid.NewGuid();
        var now = BaseTime;

        await store.AddAsync(CreatePendingEnvelope(commandId, now));

        var leased = await store.LeasePendingAsync(new InboxLeaseRequest
        {
            BatchSize = 1,
            LeaseOwner = "worker-1",
            Now = now,
            LeaseDuration = TimeSpan.Zero
        });

        leased.Should().ContainSingle();
        leased[0].LeaseExpiresAt.Should().Be(now.AddMinutes(5));
    }

    [Fact]
    public async Task LeasePendingAsync_WhenNowIsOmitted_ShouldUseTimeProviderForLeaseExpiry()
    {
        var clock = new ManualTimeProvider(BaseTime);
        var store = new InMemoryInboxStore(timeProvider: clock);
        var commandId = Guid.NewGuid();

        await store.AddAsync(CreatePendingEnvelope(commandId, BaseTime));

        await store.LeasePendingAsync(new InboxLeaseRequest
        {
            BatchSize = 1,
            LeaseOwner = "stale-worker",
            Now = BaseTime,
            LeaseDuration = TimeSpan.FromSeconds(30)
        });

        clock.Advance(TimeSpan.FromMinutes(1));

        var reclaimed = await store.LeasePendingAsync(new InboxLeaseRequest
        {
            BatchSize = 1,
            LeaseOwner = "fresh-worker",
            Now = default,
            LeaseDuration = TimeSpan.FromMinutes(1)
        });

        reclaimed.Should().ContainSingle();
        reclaimed[0].LeaseOwner.Should().Be("fresh-worker");
        reclaimed[0].AttemptCount.Should().Be(2);
    }

    private static InboxEnvelope CreatePendingEnvelope(Guid commandId, DateTimeOffset createdAt)
    {
        return new InboxEnvelope
        {
            Id = commandId,
            ContractName = "tests.commands.ship",
            ContractVersion = 1,
            Payload = "{\"orderId\":\"1\"}",
            CreatedAt = createdAt,
            AttemptCount = 0,
            Status = InboxStatus.Pending,
            IdempotencyKey = $"ship:{commandId:N}"
        };
    }

    /// <summary>
    ///     A controllable UTC clock for lease expiry tests.
    /// </summary>
    private sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow;

        /// <summary>
        ///     Initializes a new instance of the <see cref="ManualTimeProvider" /> class.
        /// </summary>
        /// <param name="initial">The initial UTC timestamp.</param>
        public ManualTimeProvider(DateTimeOffset initial)
        {
            _utcNow = initial;
        }

        /// <summary>
        ///     Advances the clock by the supplied duration.
        /// </summary>
        /// <param name="amount">The duration to add.</param>
        public void Advance(TimeSpan amount)
        {
            _utcNow = _utcNow.Add(amount);
        }

        /// <inheritdoc />
        public override DateTimeOffset GetUtcNow()
        {
            return _utcNow;
        }
    }
}
